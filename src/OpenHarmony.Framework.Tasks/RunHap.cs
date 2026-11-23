using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;

namespace OpenHarmony.Framework.Tasks;

public sealed class RunHap : HapTask
{
    public string Device { get; set; } = "";
    public bool DevelopmentSigning { get; set; }
    public string SignedHapPath { get; set; } = "";

    private string Hdc => Path.Combine(Toolchains, "hdc" + ExecutableExtension);
    private string OnDevice(params string[] args) => RunTool(new ProcessStartInfo(Hdc, ["-t", Device, .. args]));

    protected override void ExecuteHap()
    {
        RequireFile(Hdc, "Install the HarmonyOS toolchains component.");
        var targets = RunTool(new ProcessStartInfo(Hdc, ["list", "targets"]))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line != "[Empty]").ToArray();
        if (Device.Length == 0)
        {
            if (targets.Length != 1)
                throw new InvalidOperationException("Connect one device or select an HDC target with OpenHarmonyDevice.");
            Device = targets[0];
        }
        if (!targets.Contains(Device))
            throw new InvalidOperationException($"HDC target '{Device}' is not connected. Start the emulator or connect the device.");
        string architecture = OnDevice("shell", "uname", "-m");
        if (architecture != (Abi == "x86_64" ? "x86_64" : "aarch64"))
            throw new InvalidOperationException($"Device architecture is {architecture}. Set OpenHarmonyAbi to match the device (x86_64 or arm64-v8a).");

        string signedHap;
        if (SignedHapPath.Length > 0)
            signedHap = FullPath(SignedHapPath);
        else if (DevelopmentSigning)
            signedHap = SignForDevice();
        else
            throw new InvalidOperationException("Set OpenHarmonySignedHapPath to a signed HAP, or enable development signing.");
        RequireFile(signedHap, "The signed HAP was not found.");
        string install = OnDevice("install", "-r", signedHap);
        if (!install.Contains("successfully", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("HAP installation failed: " + install);
        OnDevice("shell", "aa", "force-stop", BundleName);
        string start = OnDevice("shell", "aa", "start", "-a", "EntryAbility", "-b", BundleName);
        if (!start.Contains("success", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ability start failed: " + start);
        Log.LogMessage(MessageImportance.High, "Started {0} on {1}.", BundleName, Device);
    }

    private string SignForDevice()
    {
        string lib = Path.Combine(Toolchains, "lib");
        string sdkKeyStore = Path.Combine(lib, "OpenHarmony.p12");
        RequireFile(sdkKeyStore, "SDK development signing is unavailable. Provide OpenHarmonySignedHapPath.");
        string signing = Directory.CreateDirectory(Path.Combine(WorkDirectory, "signing")).FullName;
        string keyStore = Path.Combine(signing, "development.p12");
        File.Copy(sdkKeyStore, keyStore, true);
        string root = Path.Combine(signing, "root.cer");
        string ca = Path.Combine(signing, "ca.cer");
        string chain = Path.Combine(signing, "app-chain.pem");
        string keytool = Path.Combine(JavaHome, "bin", "keytool" + ExecutableExtension);
        void Export(string alias, string path) => RunTool(new ProcessStartInfo(keytool,
            ["-exportcert", "-rfc", "-alias", alias, "-keystore", keyStore, "-storepass", "123456", "-file", path]));
        void Sign(params string[] args) => RunTool(new ProcessStartInfo(Java,
            ["-jar", Path.Combine(lib, "hap-sign-tool.jar"), .. args]));

        Export("openharmony application root ca", root);
        Export("openharmony application ca", ca);
        Sign("generate-app-cert", "-keyAlias", "openharmony application release", "-keyPwd", "123456",
            "-issuer", "C=CN,O=OpenHarmony,OU=OpenHarmony Team,CN=OpenHarmony Application CA",
            "-issuerKeyAlias", "openharmony application ca", "-issuerKeyPwd", "123456",
            "-subject", "C=CN,O=OpenHarmony,OU=OpenHarmony Team,CN=Dotnet Development", "-validity", "30",
            "-signAlg", "SHA256withECDSA", "-keystoreFile", keyStore, "-keystorePwd", "123456",
            "-rootCaCertFile", root, "-subCaCertFile", ca, "-outForm", "certChain", "-outFile", chain);
        var certificates = Regex.Matches(File.ReadAllText(chain), "-----BEGIN CERTIFICATE-----.*?-----END CERTIFICATE-----", RegexOptions.Singleline);
        if (certificates.Count != 3)
            throw new InvalidOperationException("Expected a three-certificate development chain.");
        string? udid = OnDevice("shell", "bm", "get", "-u").Split('\n', StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => Regex.IsMatch(line, @"\A[0-9A-Fa-f]{64}\z"));
        if (udid is null)
            throw new InvalidOperationException("Cannot read the target device UDID.");

        var profile = JsonNode.Parse(File.ReadAllText(Path.Combine(lib, "UnsgnedDebugProfileTemplate.json")))!.AsObject();
        var now = DateTimeOffset.UtcNow;
        profile["uuid"] = Guid.NewGuid().ToString();
        profile["validity"]!["not-before"] = now.AddDays(-1).ToUnixTimeSeconds();
        profile["validity"]!["not-after"] = now.AddDays(30).ToUnixTimeSeconds();
        profile["bundle-info"]!["bundle-name"] = BundleName;
        profile["bundle-info"]!["development-certificate"] = certificates[0].Value + "\n";
        profile["debug-info"]!["device-ids"] = new JsonArray(JsonValue.Create(udid));
        string profilePath = Path.Combine(signing, "profile.json");
        File.WriteAllText(profilePath, profile.ToJsonString());

        string signedProfile = Path.Combine(signing, "profile.p7b");
        string signedHap = Path.Combine(PublishDirectory, AssemblyName + "-signed.hap");
        Sign("sign-profile", "-mode", "localSign", "-keyAlias", "openharmony application profile debug", "-keyPwd", "123456",
            "-profileCertFile", Path.Combine(lib, "OpenHarmonyProfileDebug.pem"), "-inFile", profilePath,
            "-signAlg", "SHA256withECDSA", "-keystoreFile", keyStore, "-keystorePwd", "123456", "-outFile", signedProfile);
        Sign("sign-app", "-mode", "localSign", "-keyAlias", "openharmony application release", "-keyPwd", "123456",
            "-appCertFile", chain, "-profileFile", signedProfile, "-inFile", HapPath, "-signAlg", "SHA256withECDSA",
            "-keystoreFile", keyStore, "-keystorePwd", "123456", "-outFile", signedHap, "-compatibleVersion", "15");
        Sign("verify-app", "-inFile", signedHap, "-outCertChain", Path.Combine(signing, "verified-chain.cer"),
            "-outProfile", Path.Combine(signing, "verified-profile.p7b"));
        return signedHap;
    }
}
