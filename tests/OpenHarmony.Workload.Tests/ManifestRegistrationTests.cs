using Chaldea.OpenHarmony.Workload;
using Xunit;

namespace OpenHarmony.Workload.Tests;

public sealed class ManifestRegistrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "workload-registration-" + Guid.NewGuid().ToString("N"));
    private const string Manifest = """{"version":"11.0.0-preview.1","workloads":{"harmonyos":{}},"packs":{}}""";
    private const string Targets = "<Project />";
    private string RegistrationRoot => Path.Combine(root, "sdk-manifests/11.0.100/chaldea.openharmony.workload");

    private ManifestRegistration CreateRegistration()
    {
        Directory.CreateDirectory(Path.Combine(root, "sdk/11.0.100"));
        File.WriteAllText(Path.Combine(root, SdkInstallation.DotnetFileName), "");
        return new(new(root, "11.0.100", "11.0.100"), "11.0.100");
    }

    [Fact]
    public void DiscoversTheSdkSelectedByGlobalJsonRatherThanTheNewestInstalledSdk()
    {
        var sdk = SdkInstallation.Discover("11.0.100", $"10.0.301 [{root}/sdk]\n11.0.100 [{root}/sdk]\n11.0.200 [{root}/sdk]");
        Assert.Equal(Path.GetFullPath(root), sdk.Root);
        Assert.Equal("11.0.100", sdk.FeatureBand);
    }

    [Fact]
    public void RejectsHostFallbackToAnotherRoot()
    {
        Assert.Throws<InvalidOperationException>(() => SdkInstallation.Discover("11.0.100", $"11.0.100 [{root}/sdk]", root + "-other"));
    }

    [Theory]
    [InlineData("10.0.301")]
    [InlineData("11.0.100-rc.1.26425.128")]
    [InlineData("12.0.100")]
    public void RejectsSdkOutsideTheReleasedNet11Line(string version)
    {
        Assert.Throws<InvalidOperationException>(() => SdkInstallation.Discover(version, $"{version} [{root}/sdk]"));
    }

    [Fact]
    public void RegistrationIsIdempotentAndUnregisterPreservesOtherManifests()
    {
        var registration = CreateRegistration();
        string unrelated = Path.Combine(root, "sdk-manifests/11.0.100/another/1.0.0/WorkloadManifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(unrelated)!);
        File.WriteAllText(unrelated, """{"workloads":{/* SDK manifest comment */},}""");
        int configurations = 0;
        registration.Register(Manifest, Targets, () => configurations++);
        registration.Register(Manifest, Targets, () => configurations++);
        Assert.Equal(1, configurations);
        Assert.Equal(Manifest, File.ReadAllText(Path.Combine(RegistrationRoot, "11.0.0-preview.1/WorkloadManifest.json")));
        registration.Unregister();
        Assert.False(Directory.Exists(RegistrationRoot));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void CliConfigurationFailureDoesNotLeaveAPartialManifest()
    {
        var registration = CreateRegistration();
        Assert.Throws<InvalidOperationException>(() => registration.Register(Manifest, Targets, () => throw new InvalidOperationException("CLI failed")));
        Assert.False(Directory.Exists(RegistrationRoot));
        Assert.Empty(Directory.EnumerateDirectories(root, ".harmonyos-workload-*"));
    }

    [Theory]
    [InlineData("metadata/workloads/11.0.100/InstalledWorkloads")]
    [InlineData("metadata/workloads/X64/11.0.100/InstalledWorkloads")]
    [InlineData("metadata/workloads/Arm64/11.0.100/InstalledWorkloads")]
    public void InstalledWorkloadMustBeUninstalledBeforeRemovalOrVersionChange(string recordDirectory)
    {
        var registration = CreateRegistration();
        registration.Register(Manifest, Targets, () => { });
        Directory.CreateDirectory(Path.Combine(root, recordDirectory));
        File.WriteAllText(Path.Combine(root, recordDirectory, "harmonyos"), "");
        Assert.Throws<InvalidOperationException>(registration.Unregister);
        Assert.Throws<InvalidOperationException>(() => registration.Register(Manifest.Replace("preview.1", "preview.2"), Targets, () => { }));
        registration.Register(Manifest, Targets, () => throw new InvalidOperationException("Idempotent init must not modify installed state"));
    }

    [Fact]
    public void RejectsOldUpstreamManifestWithSameWorkloadId()
    {
        var registration = CreateRegistration();
        string oldRoot = Path.Combine(root, "sdk-manifests/11.0.100/openharmony.net.workload/11.0.0");
        Directory.CreateDirectory(oldRoot);
        File.WriteAllText(Path.Combine(oldRoot, "WorkloadManifest.json"), Manifest);
        Assert.Throws<InvalidOperationException>(() => registration.Register(Manifest, Targets, () => { }));
        Assert.False(Directory.Exists(RegistrationRoot));
    }

    [Fact]
    public void RejectsMsiManagedSdkWithoutModifyingItsState()
    {
        var registration = CreateRegistration();
        string installer = Path.Combine(root, "metadata/workloads/11.0.100/installertype");
        Directory.CreateDirectory(installer);
        File.WriteAllText(Path.Combine(installer, "msi"), "");
        Assert.Throws<InvalidOperationException>(() => registration.Register(Manifest, Targets, () => { }));
        Assert.False(Directory.Exists(RegistrationRoot));
    }

    [Fact]
    public void VersionCannotEscapeTheManifestDirectory()
    {
        var registration = CreateRegistration();
        Assert.Throws<InvalidOperationException>(() => registration.Register(Manifest.Replace("11.0.0-preview.1", "../../outside"), Targets, () => { }));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
