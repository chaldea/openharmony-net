using System.Text.Json;
using System.Text.RegularExpressions;

namespace Chaldea.OpenHarmony.Workload;

public sealed record SdkInstallation(string Root, string Version, string FeatureBand)
{
    public static string DotnetFileName => OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    public string DotnetPath => Path.Combine(Root, DotnetFileName);
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static SdkInstallation Discover(string version, string sdkList, string? requestedRoot = null)
    {
        Match match = Regex.Match(version, @"^10\.0\.(\d+)$");
        if (!match.Success) throw new InvalidOperationException($"A released .NET 10 SDK is required; dotnet selected '{version}'. Check PATH and global.json.");
        string band = "10.0." + (int.Parse(match.Groups[1].Value) / 100 * 100);
        string prefix = version + " [";
        string[] locations = sdkList.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith(']'))
            .Select(line => Path.GetFullPath(Path.Combine(line[prefix.Length..^1], "..")))
            .Distinct(PathComparer).ToArray();
        if (locations.Length != 1) throw new InvalidOperationException($"Cannot locate the selected SDK {version} in dotnet --list-sdks.");
        string root = locations[0];
        if (requestedRoot is not null && !PathComparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(requestedRoot)), root))
            throw new InvalidOperationException("The selected SDK belongs to a different dotnet root. Check global.json and --dotnet-root.");
        return new(root, version, band);
    }
}

public sealed class ManifestRegistration(SdkInstallation sdk, string supportedFeatureBand)
{
    public const string ManifestId = "chaldea.openharmony.workload";
    private string ManifestRoot => Path.Combine(sdk.Root, "sdk-manifests", sdk.FeatureBand, ManifestId);
    private IEnumerable<string> MetadataRoots => new[]
    {
        Path.Combine(sdk.Root, "metadata", "workloads", "X64", sdk.FeatureBand),
        Path.Combine(sdk.Root, "metadata", "workloads", "Arm64", sdk.FeatureBand),
        Path.Combine(sdk.Root, "metadata", "workloads", sdk.FeatureBand)
    };
    private bool IsInstalled => MetadataRoots.Any(root => File.Exists(Path.Combine(root, "InstalledWorkloads", "harmonyos")));

    public void Register(string manifest, string targets, Action configureManifestMode)
    {
        ValidateSdk();
        using var json = JsonDocument.Parse(manifest);
        string version = json.RootElement.GetProperty("version").GetString()!;
        if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$"))
            throw new InvalidOperationException("Invalid embedded manifest version.");
        string destination = Path.Combine(ManifestRoot, version);
        string manifestFile = Path.Combine(destination, "WorkloadManifest.json");
        string targetsFile = Path.Combine(destination, "WorkloadManifest.targets");
        if (File.Exists(manifestFile) && File.Exists(targetsFile) &&
            File.ReadAllText(manifestFile) == manifest && File.ReadAllText(targetsFile) == targets)
            return;
        if (IsInstalled)
            throw new InvalidOperationException("Uninstall harmonyos before changing its registration: dotnet workload uninstall harmonyos.");
        if (Directory.Exists(ManifestRoot) && Directory.EnumerateFileSystemEntries(ManifestRoot).Any())
            throw new InvalidOperationException("A different registration exists. Run harmonyos-workload unregister before initializing this version.");
        string manifests = Path.Combine(sdk.Root, "sdk-manifests");
        if (Directory.Exists(manifests))
        {
            foreach (string file in Directory.EnumerateFiles(manifests, "WorkloadManifest.json", SearchOption.AllDirectories))
            {
                if (file.StartsWith(ManifestRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                // The SDK's own manifests permit JSON comments and trailing commas.
                using var other = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
                {
                    AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
                });
                if (other.RootElement.TryGetProperty("workloads", out var workloads) && workloads.TryGetProperty("harmonyos", out _))
                    throw new InvalidOperationException($"Another manifest already declares harmonyos: {file}. Remove its registration before continuing.");
            }
        }
        // Stage alongside sdk-manifests, so the resolver never sees half a manifest.
        string staging = Path.Combine(sdk.Root, ".harmonyos-workload-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "WorkloadManifest.json"), manifest);
            File.WriteAllText(Path.Combine(staging, "WorkloadManifest.targets"), targets);
            configureManifestMode();
            Directory.CreateDirectory(ManifestRoot);
            Directory.Move(staging, destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    public void Unregister()
    {
        ValidateSdk();
        if (IsInstalled) throw new InvalidOperationException("Run dotnet workload uninstall harmonyos before unregistering its manifest.");
        if (Directory.Exists(ManifestRoot)) Directory.Delete(ManifestRoot, recursive: true);
    }

    private void ValidateSdk()
    {
        if (sdk.FeatureBand != supportedFeatureBand)
            throw new InvalidOperationException($"This tool supports SDK feature band {supportedFeatureBand}; selected {sdk.FeatureBand}. Use a matching SDK or tool release.");
        if (!File.Exists(sdk.DotnetPath) || !Directory.Exists(Path.Combine(sdk.Root, "sdk", sdk.Version)))
            throw new InvalidOperationException("The selected standalone SDK is incomplete.");
        if (MetadataRoots.Any(root => File.Exists(Path.Combine(root, "installertype", "msi"))))
            throw new InvalidOperationException("MSI-managed workload installations are unsupported. Extract the official .NET SDK Windows x64 ZIP and select it with --dotnet-root.");
        // Never follow a link when registering or recursively removing our own directory.
        for (string? path = ManifestRoot; path is not null; path = Path.GetDirectoryName(path))
        {
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"SDK manifest paths must not contain directory links: {path}");
        }
        if (Directory.Exists(ManifestRoot) && Directory.EnumerateFileSystemEntries(ManifestRoot, "*", SearchOption.AllDirectories)
            .Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
            throw new InvalidOperationException("The registered manifest contains a link; remove it manually.");
    }
}
