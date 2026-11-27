using Chaldea.OpenHarmony.Workload;
using Xunit;

namespace OpenHarmony.Workload.Tests;

public sealed class EmulatorCommandsTests : IDisposable
{
    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("ohos-emulator-");

    public void Dispose() => temp.Delete(recursive: true);

    private const string DefaultVersion = "HarmonyOS 7.0.0(26.0.0)";
    private const string ImagePath = "system-image/HarmonyOS-6.0.31/phone_all_x86";
    private string Images => Path.Combine(temp.FullName, "image root");
    private string Devices => Path.Combine(temp.FullName, "instance root");
    private Dictionary<string, string> Options => new() { ["--image-root"] = Images, ["--instance-path"] = Devices };

    private static void WriteImage(string root)
    {
        string image = Directory.CreateDirectory(Path.Combine(root, ImagePath)).FullName;
        File.WriteAllText(Path.Combine(image, "sdk-pkg.json"), "{}");
        File.WriteAllBytes(Path.Combine(image, "system.img"), [42]);
    }

    private void WriteDevice()
    {
        string device = Directory.CreateDirectory(Path.Combine(Devices, "MyPhone")).FullName;
        File.WriteAllText(Path.Combine(device, "config.ini"), $"imageSubPath={ImagePath}");
        File.WriteAllText(device + ".ini", $"path={device}");
    }

    [Theory]
    [InlineData(DefaultVersion, false)]
    [InlineData("HarmonyOS 6.1.0(23)", true)]
    public void DownloadsAnAvailableImageBeforeCreatingTheDevice(string version, bool interrupted)
    {
        string image = Path.Combine(Images, ImagePath);
        if (interrupted)
        {
            Directory.CreateDirectory(image);
            File.WriteAllText(Path.Combine(image, "incomplete.zip"), "partial");
            File.WriteAllText(Path.Combine(image, "sdk-pkg.json"), "{}");
        }
        var calls = new List<string>();
        var options = Options;
        options["--http-proxy"] = "http://localhost:8080";
        int code = EmulatorCommands.Create("MyPhone", options, (args, capture) =>
        {
            calls.Add(args[0]);
            switch (args[0])
            {
                case "-imageList":
                    Assert.True(capture);
                    Assert.Equal(["-imageList", "-deviceType", "phone", "-imageRoot", Images, "-http_proxy", options["--http-proxy"]], args);
                    return (0, $$"""[{"deviceType":"phone","osVersion":"{{version}}","releaseType":"Release","downloaded":"{{(interrupted ? "true" : "false")}}"}]""");
                case "-install":
                    string staging = args[6];
                    Assert.Equal(["-install", "-deviceType", "phone", "-osVersion", version, "-imageRoot", staging, "-http_proxy", options["--http-proxy"]], args);
                    Assert.Empty(Directory.EnumerateFileSystemEntries(staging));
                    WriteImage(staging);
                    return (0, "");
                case "-create":
                    Assert.Equal(["-create", "MyPhone", "-deviceType", "phone", "-osVersion", version, "-instancePath", Devices, "-imageRoot", Images, .. (version == DefaultVersion ? new[] { "-screenProfile", "Pura 90 Pro" } : Array.Empty<string>())], args);
                    Assert.True(File.Exists(Path.Combine(image, "system.img")));
                    WriteDevice();
                    return (0, "");
                default:
                    throw new ArgumentException(args[0]);
            }
        });
        Assert.Equal(0, code);
        Assert.Equal(["-imageList", "-install", "-create"], calls);
        Assert.Empty(Directory.GetDirectories(Images, ".download-*"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void FailedInstallCannotUseAnUnrelatedCachedImage(int exitCode)
    {
        WriteImage(Images);
        var options = Options;
        options["--os-version"] = "another version";
        var calls = new List<string>();
        (int, string) Invoke(string[] args, bool capture)
        {
            calls.Add(args[0]);
            if (args[0] == "-create")
                return (0, "Cannot find image, please verify your SDK installation.\nDevice create fail.");
            Assert.Equal("-install", args[0]);
            // A cancelled install can return zero and leave just a package manifest.
            string partial = Directory.CreateDirectory(Path.Combine(args[6], ImagePath)).FullName;
            File.WriteAllText(Path.Combine(partial, "sdk-pkg.json"), "{}");
            return (exitCode, "");
        }
        if (exitCode == 0)
            Assert.Contains("did not install", Assert.Throws<InvalidOperationException>(() => EmulatorCommands.Create("MyPhone", options, Invoke)).Message);
        else
            Assert.Equal(exitCode, EmulatorCommands.Create("MyPhone", options, Invoke));
        Assert.Equal(["-create", "-install"], calls);
        Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(Path.Combine(Images, ImagePath, "system.img")));
        Assert.Empty(Directory.GetDirectories(Images, ".download-*"));
        Assert.Empty(Directory.GetFileSystemEntries(Devices));
    }

    [Fact]
    public void CreatesFromCacheWithoutQueryingTheCatalogOrDownloading()
    {
        WriteImage(Images);
        Assert.Equal(0, EmulatorCommands.Create("MyPhone", Options, (args, capture) =>
        {
            Assert.Equal(["-create", "MyPhone", "-deviceType", "phone", "-osVersion", DefaultVersion, "-instancePath", Devices, "-imageRoot", Images, "-screenProfile", "Pura 90 Pro"], args);
            WriteDevice();
            return (0, "");
        }));
    }

    [Fact]
    public void CreateRequiresValuesForImageOptions()
    {
        Assert.Equal(1, Program.Main(["emulator", "create", "Phone", "--os-version"]));
        Assert.Equal(1, Program.Main(["emulator", "create", "Phone", "--device-type", "--os-version", "anything"]));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("bad/name")]
    [InlineData("--bad")]
    public void CommandsRejectInvalidDeviceNames(string name)
    {
        Assert.Equal(1, Program.Main(["emulator", "create", name, "--device-type", "phone", "--os-version", "HarmonyOS 7.0.0(26.0.0)"]));
    }
}
