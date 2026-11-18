using Xunit;

namespace PublishAotCross.Tests;

public sealed class TargetsTests
{
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "OpenHarmony.slnx")))
                {
                    return Path.Combine(directory.FullName, "src", "PublishAotCross");
                }
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the PublishAotCross repository root.");
        }
    }

    [Fact]
    public void OpenHarmonyTargetAcceptsTheSupportedApiProfilesAndAbis()
    {
        var targets = File.ReadAllText(Path.Combine(RepositoryRoot, "package", "Crosscompile.targets"));

        Assert.Contains("OpenHarmonyApiLevel", targets);
        Assert.Contains("OpenHarmonyApiLevel Condition=\"'$(OpenHarmonyApiLevel)' == ''\">15", targets);
        Assert.Contains("one of 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, or 26", targets);
        Assert.Contains("API 25 is intentionally unsupported because no compatible SDK/image is available.", targets);
        Assert.Contains("arm64-v8a", targets);
        Assert.Contains("x86_64", targets);
        Assert.Contains("aarch64-linux-ohos", targets);
        Assert.Contains("x86_64-linux-ohos", targets);
    }

    [Fact]
    public void OpenHarmonyUsesTheSdkToolchainAndDoesNotPrependFakeClang()
    {
        var targets = File.ReadAllText(Path.Combine(RepositoryRoot, "package", "Crosscompile.targets"));

        Assert.Contains("OpenHarmonyClang", targets);
        Assert.Contains("OpenHarmonyClangXX", targets);
        Assert.Contains("OpenHarmonyLinker", targets);
        Assert.Contains("--sysroot", targets);
        Assert.Contains("LinkerFlavor>lld", targets);
        Assert.Contains("OpenHarmonySdkRoot is required", targets);
        Assert.Contains("Condition=\"'$(OpenHarmonyTarget)' != 'true'\"", targets);
    }

    [Fact]
    public void OpenHarmonyToolchainIsConfiguredBeforeNativeAotSetup()
    {
        var targets = File.ReadAllText(Path.Combine(RepositoryRoot, "package", "Crosscompile.targets"));

        Assert.Contains("Name=\"ConfigureOpenHarmonyToolchain\"", targets);
        Assert.Contains("BeforeTargets=\"SetupOSSpecificProps\"", targets);
        Assert.Contains("PrependPath Value=\"$(OpenHarmonyNativeRoot)\\llvm\\bin\"", targets);
        Assert.Contains("<CppLinker>clang++</CppLinker>", targets);
        Assert.Contains("<CompressSymbols>false</CompressSymbols>", targets);
        Assert.Contains("<NetCoreAppNativeLibrary Remove=\"System.Net.Security.Native\" />", targets);
        Assert.Contains("<DirectPInvoke Remove=\"System.Net.Security.Native\" />", targets);
        Assert.Contains("<LinkerArg Remove=\"$(IlcFrameworkNativePath)libSystem.Net.Security.Native.a\" />", targets);
        Assert.Contains("Name=\"RemoveOpenHarmonyUnavailableNativeLibraries\"", targets);
        Assert.Contains("AfterTargets=\"IlcCompile\"", targets);
    }

    [Fact]
    public void OpenHarmonySdkIsResolvedFromManifestBeforeToolchainConfiguration()
    {
        var targets = File.ReadAllText(Path.Combine(RepositoryRoot, "package", "Crosscompile.targets"));

        Assert.Contains("Name=\"ResolveOpenHarmonySdk\"", targets);
        Assert.Contains("OpenHarmonySdkResolver\\OpenHarmonySdkResolver.csproj", targets);
        Assert.Contains("--sdk-root", targets);
        Assert.Contains("--api", targets);
        Assert.Contains("--output", targets);
        Assert.Contains("<Import Project=\"$(OpenHarmonySdkResolverPropsPath)\"", targets);
        Assert.Contains("<XmlPeek", targets);
        Assert.Contains("DependsOnTargets=\"ValidateOpenHarmonyResolvedSdk\"", targets);
        Assert.DoesNotContain("<OpenHarmonySdkFolder>$(OpenHarmonyApiLevel)</OpenHarmonySdkFolder>", targets);
    }

    [Fact]
    public void NugetPackageCarriesTheSdkResolver()
    {
        var packageProject = File.ReadAllText(
            Path.Combine(RepositoryRoot, "package", "OpenHarmony.NET.PublishAotCross.csproj"));

        Assert.Contains("OpenHarmonySdkResolver/**", packageProject);
        Assert.Contains("build/OpenHarmonySdkResolver", packageProject);
    }

    [Fact]
    public void HelloProjectTargetsDotNet10AndOpenHarmonyProperties()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryRoot, "..", "..", "examples", "CrossCompileHello", "Hello.csproj"));

        Assert.Contains("net10.0", project);
        Assert.Contains("OpenHarmony.NET.PublishAotCross.targets", project);
        Assert.Contains("PublishAotCross.Tests/**", project);
    }
}
