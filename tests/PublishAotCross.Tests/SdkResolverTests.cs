using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using OpenHarmony.SdkResolution;
using Xunit;

namespace PublishAotCross.Tests;

public sealed class SdkResolverTests
{
    [Fact]
    public void Resolves_by_manifest_api_instead_of_folder_name_and_writes_props()
    {
        using TemporarySdk sdk = TemporarySdk.Create();
        sdk.AddPackage(Path.Combine("nested", "api14-copy"), 14, "14.0.1");

        OpenHarmonySdkResolution result = OpenHarmonySdkResolver.Resolve(sdk.Root, 14);
        string propsPath = Path.Combine(sdk.Root, "resolved.props");
        result.WriteProps(propsPath);

        Assert.Equal("5.0.2", result.FolderName);
        Assert.Equal(14, result.ApiLevel);
        Assert.Equal(Path.Combine(sdk.Root, "5.0.2", "native"), result.NativeRoot);
        Assert.Equal(HashFile(result.ManifestPath), result.ManifestSha256);

        XElement properties = XDocument.Load(propsPath).Root!.Element("PropertyGroup")!;
        Assert.Equal(result.ApiLevel.ToString(), Property("OpenHarmonyApiLevel"));
        Assert.Equal(result.FolderName, Property("OpenHarmonySdkFolder"));
        Assert.Equal(result.NativeRoot, Property("OpenHarmonyNativeRoot"));
        Assert.Equal(result.ManifestPath, Property("OpenHarmonySdkManifestPath"));
        Assert.Equal(result.ManifestSha256, Property("OpenHarmonySdkManifestSha256"));
        Assert.Equal(result.PackageVersion, Property("OpenHarmonySdkPackageVersion"));
        Assert.Equal(result.ReleaseType, Property("OpenHarmonySdkReleaseType"));

        string Property(string name) => properties.Element(name)!.Value;
    }

    [Fact]
    public void Rejects_duplicate_manifest_api_versions()
    {
        using TemporarySdk sdk = TemporarySdk.Create();
        sdk.AddPackage("5.0.2-copy", 14, "14.0.0");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => OpenHarmonySdkResolver.Resolve(sdk.Root, 14));

        Assert.Contains("multiple Native SDK manifests", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_api25_with_the_explicit_contract_message()
    {
        using TemporarySdk sdk = TemporarySdk.Create();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => OpenHarmonySdkResolver.Resolve(sdk.Root, 25));

        Assert.Equal(
            "API 25 is intentionally unsupported because no compatible SDK/image is available.",
            exception.Message);
    }

    [Fact]
    public void Rejects_missing_toolchain_inputs_before_consuming_the_sdk()
    {
        using TemporarySdk sdk = TemporarySdk.Create();
        File.Delete(Path.Combine(sdk.Root, "5.0.2", "native", "llvm", "bin", "ld.lld.exe"));

        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(
            () => OpenHarmonySdkResolver.Resolve(sdk.Root, 14));

        Assert.Contains("ld.lld", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class TemporarySdk : IDisposable
    {
        private TemporarySdk(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporarySdk Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "openharmony-sdk-resolver-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            TemporarySdk sdk = new(root);
            sdk.AddPackage("5.0.1", 13, "13.0.0");
            sdk.AddPackage("5.0.2", 14, "14.0.0");
            sdk.AddPackage("26.0.0", 26, "26.0.0");
            return sdk;
        }

        public void AddPackage(string folder, int api, string packageVersion)
        {
            string native = Path.Combine(Root, folder, "native");
            string sysroot = Path.Combine(native, "sysroot");
            string bin = Path.Combine(native, "llvm", "bin");
            Directory.CreateDirectory(Path.Combine(sysroot, "usr", "include"));
            Directory.CreateDirectory(bin);
            Directory.CreateDirectory(Path.Combine(native, "build", "cmake"));
            File.WriteAllText(Path.Combine(sysroot, "usr", "include", "sample.h"), "sample");
            File.WriteAllText(Path.Combine(bin, "clang.exe"), string.Empty);
            File.WriteAllText(Path.Combine(bin, "ld.lld.exe"), string.Empty);
            File.WriteAllText(Path.Combine(native, "build", "cmake", "ohos.toolchain.cmake"), "toolchain");
            File.WriteAllText(
                Path.Combine(native, "oh-uni-package.json"),
                JsonSerializer.Serialize(new { apiVersion = api.ToString(), version = packageVersion, releaseType = "Release" }));
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
