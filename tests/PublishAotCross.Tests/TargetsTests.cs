using System.Diagnostics;
using System.Security;
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
        Assert.Contains("PrependPath Value=\"$(OpenHarmonyNativeRoot)/llvm/bin\"", targets);
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
    public void HelloProjectTargetsDotNet11AndOpenHarmonyProperties()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryRoot, "..", "..", "examples", "CrossCompileHello", "Hello.csproj"));

        Assert.Contains("net11.0", project);
        Assert.Contains("OpenHarmony.NET.PublishAotCross.targets", project);
        Assert.Contains("PublishAotCross.Tests/**", project);
    }

    [Theory]
    [InlineData("x86_64", "linux-musl-x64", "x86_64-linux-ohos")]
    [InlineData("arm64-v8a", "linux-musl-arm64", "aarch64-linux-ohos")]
    public async Task ToolchainTargetsSelectTheOhosLinkerWithoutLegacyLinuxLibraries(string abi, string rid, string triple)
    {
        string root = Path.Combine(Path.GetTempPath(), "ohos-targets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "probe.proj");
            string output = Path.Combine(root, "linker.txt");
            string targets = Path.Combine(RepositoryRoot, "package", "Crosscompile.targets");
            string native = SecurityElement.Escape(root)!;
            File.WriteAllText(project, $$"""
                <Project>
                  <PropertyGroup>
                    <OpenHarmonyTarget>true</OpenHarmonyTarget>
                    <RuntimeIdentifier>{{rid}}</RuntimeIdentifier>
                    <OpenHarmonyAbi>{{abi}}</OpenHarmonyAbi>
                    <OpenHarmonyNativeRoot>{{native}}</OpenHarmonyNativeRoot>
                    <OpenHarmonyClangXX>{{native}}/llvm/bin/clang++</OpenHarmonyClangXX>
                    <OpenHarmonySysroot>{{native}}/sysroot</OpenHarmonySysroot>
                  </PropertyGroup>
                  <Import Project="{{SecurityElement.Escape(targets)}}" />
                  <PropertyGroup>
                    <!-- A real executable for the macOS version probe, without requiring an NDK. -->
                    <OpenHarmonyLinker>$(MSBuildBinPath)/../../dotnet$(OpenHarmonyExecutableExtension)</OpenHarmonyLinker>
                  </PropertyGroup>
                  <!-- Isolate target ordering and linker arguments from SDK discovery. -->
                  <Target Name="ValidateOpenHarmonyResolvedSdk" />
                  <Target Name="SetupOSSpecificProps" />
                  <Target Name="Probe" DependsOnTargets="SetupOSSpecificProps">
                    <WriteLinesToFile File="{{SecurityElement.Escape(output)}}" Overwrite="true"
                      Lines="$(TargetTriple);$(CppLinker);@(LinkerArg)" />
                  </Target>
                </Project>
                """);
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            };
            foreach (string argument in new[] { "msbuild", project, "-t:Probe", "-nologo", "-v:minimal" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
            string[] lines = File.ReadAllLines(output);
            Assert.Equal(triple, lines[0]);
            Assert.Equal(root + "/llvm/bin/clang++", lines[1]);
            Assert.Contains("--target=" + triple, lines);
            Assert.DoesNotContain(lines, line => line.Contains("-linux-musl") || line.Contains("-linux-gnu") || line.Contains("libs/"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IlcResponseExcludesUnavailableGssOnlyForHarmonyOS(bool harmony)
    {
        string root = Path.Combine(Path.GetTempPath(), "ohos-pinvoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "probe.proj");
            string output = Path.Combine(root, "pinvokes.txt");
            string targets = Path.Combine(RepositoryRoot, "package", "Crosscompile.targets");
            File.WriteAllText(project, $$"""
                <Project>
                  <PropertyGroup><OpenHarmonyTarget>{{harmony.ToString().ToLowerInvariant()}}</OpenHarmonyTarget></PropertyGroup>
                  <Import Project="{{SecurityElement.Escape(targets)}}" />
                  <!-- Simulate SDK setup adding direct imports after the early platform setup. -->
                  <Target Name="PopulateNativeImports">
                    <ItemGroup>
                      <DirectPInvoke Include="System.Native;System.Net.Security.Native;System.Security.Cryptography.Native.OpenSsl" />
                    </ItemGroup>
                  </Target>
                  <Target Name="WriteIlcRspFileForCompilation" DependsOnTargets="PopulateNativeImports">
                    <WriteLinesToFile File="{{SecurityElement.Escape(output)}}" Lines="@(DirectPInvoke)" Overwrite="true" />
                  </Target>
                </Project>
                """);
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            };
            foreach (string argument in new[] { "msbuild", project, "-t:WriteIlcRspFileForCompilation", "-nologo", "-v:minimal" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
            string[] imports = File.ReadAllLines(output);
            Assert.Contains("System.Native", imports);
            Assert.Contains("System.Security.Cryptography.Native.OpenSsl", imports);
            Assert.Equal(!harmony, imports.Contains("System.Net.Security.Native"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
