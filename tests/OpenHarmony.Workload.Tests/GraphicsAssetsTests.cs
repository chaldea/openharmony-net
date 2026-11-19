using System.Diagnostics;
using System.Security;
using Xunit;

namespace OpenHarmony.Workload.Tests;

public sealed class GraphicsAssetsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ohos-graphics-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("true", "", "x86_64", "linux-musl-x64", "x86_64")]
    [InlineData("true", "", "arm64-v8a", "linux-musl-arm64", "arm64-v8a")]
    [InlineData("", "ohos", "", "linux-musl-arm64", "arm64-v8a")]
    public void BothPackagesSelectOnlyTheRequestedAbi(string target, string platform, string abi, string rid, string expected)
    {
        var result = Run(target, platform, abi, rid);
        Assert.True(result.ExitCode == 0, result.Output);
        var lines = File.ReadAllLines(Path.Combine(root, "selected.txt"));
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Contains($"/native/{expected}/", line.Replace('\\', '/')));
        Assert.Contains(lines, line => line.Contains("libSkiaSharp.so"));
        Assert.Contains(lines, line => line.Contains("libHarfBuzzSharp.so"));
    }

    [Fact]
    public void OrdinaryLinuxConsumersDoNotReceiveHarmonyLibraries()
    {
        var result = Run("", "", "", "linux-musl-x64");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Empty(File.ReadAllLines(Path.Combine(root, "selected.txt")));
    }

    [Theory]
    [InlineData("x86_64", "linux-musl-arm64")]
    [InlineData("armeabi-v7a", "linux-musl-arm")]
    public void InvalidArchitectureFailsBeforeCopying(string abi, string rid)
    {
        var result = Run("true", "", abi, rid);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("HarmonyOS graphics", result.Output);
    }

    private (int ExitCode, string Output) Run(string target, string platform, string abi, string rid)
    {
        Directory.CreateDirectory(root);
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "OpenHarmony.slnx"))) repo = repo.Parent;
        Assert.NotNull(repo);
        var imports = new List<string>();
        foreach (var name in new[] { "SkiaSharp", "HarfBuzzSharp" })
        {
            var build = Path.Combine(root, name, "buildTransitive");
            Directory.CreateDirectory(Path.Combine(build, "common"));
            var id = $"Chaldea.{name}.NativeAssets.HarmonyOS";
            var wrapper = Path.Combine(build, id + ".targets");
            File.Copy(Path.Combine(repo.FullName, "src", name + ".NativeAssets.HarmonyOS", "buildTransitive", id + ".targets"), wrapper);
            File.Copy(Path.Combine(repo.FullName, "src", "HarmonyOS.NativeAssets", "HarmonyOS.NativeAssets.targets"),
                Path.Combine(build, "common", "HarmonyOS.NativeAssets.targets"));
            foreach (var architecture in new[] { "x86_64", "arm64-v8a" })
            {
                var directory = Path.Combine(root, name, "native", architecture);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, $"lib{name}.so"), architecture);
            }
            imports.Add($"<Import Project=\"{SecurityElement.Escape(wrapper)}\" />");
        }
        var project = Path.Combine(root, "consumer.proj");
        File.WriteAllText(project, $$"""
            <Project>
              <PropertyGroup>
                <OpenHarmonyTarget>{{target}}</OpenHarmonyTarget>
                <TargetPlatformIdentifier>{{platform}}</TargetPlatformIdentifier>
                <OpenHarmonyAbi>{{abi}}</OpenHarmonyAbi>
                <RuntimeIdentifier>{{rid}}</RuntimeIdentifier>
              </PropertyGroup>
              {{string.Join(Environment.NewLine, imports)}}
              <Target Name="AssignTargetPaths" />
              <Target Name="Check" DependsOnTargets="AssignTargetPaths">
                <WriteLinesToFile File="selected.txt" Lines="@(Content)" Overwrite="true" />
              </Target>
            </Project>
            """);
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "msbuild", project, "-t:Check", "-nologo", "-v:minimal" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "MSBuild did not complete.");
        return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
