using System.Collections;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using OpenHarmony.NET.Sdk;
using Xunit;

namespace OpenHarmony.Workload.Tests;

public sealed class WorkloadTasksTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ohos-workload-" + Guid.NewGuid().ToString("N"));
    private readonly TestBuildEngine engine = new();

    [Fact]
    public void OfficialCliLayoutInfersApiFromManifest()
    {
        string native = CreateSdk("sdk/default/openharmony", 26);
        var task = new ResolveSdk { BuildEngine = engine, CommandLineTools = root };
        Assert.True(task.Execute());
        Assert.Equal(26, task.ResolvedApiLevel);
        Assert.Equal(native, task.NativeRoot);
        Assert.Equal(64, task.ManifestSha256.Length);
    }

    [Fact]
    public void ExplicitSdkRootOverridesCliAndSelectsRequestedApi()
    {
        CreateSdk("sdk/default/openharmony", 26);
        string native = CreateSdk("custom/release-fifteen", 15);
        var task = new ResolveSdk
        {
            BuildEngine = engine, CommandLineTools = root,
            SdkRoot = Path.Combine(root, "custom"), ApiLevel = 15
        };
        Assert.True(task.Execute());
        Assert.Equal(native, task.NativeRoot);
        Assert.Equal(15, task.ResolvedApiLevel);
    }

    [Fact]
    public void ComponentParentCanBeUsedAsSdkRoot()
    {
        string native = CreateSdk("openharmony", 26);
        var task = new ResolveSdk { BuildEngine = engine, SdkRoot = Path.GetDirectoryName(native)! };
        Assert.True(task.Execute());
        Assert.Equal(native, task.NativeRoot);
    }

    [Fact]
    public void MissingSdkReportsActionableError()
    {
        var task = new ResolveSdk { BuildEngine = engine };
        Assert.False(task.Execute());
        var error = Assert.Single(engine.Errors);
        Assert.Equal("OHOS1001", error.Code);
        Assert.Contains("OHOS_COMMAND_LINE_TOOLS", error.Message);
    }

    [Fact]
    public void MultipleInstalledApisRequireAnExplicitSelection()
    {
        CreateSdk("15", 15);
        CreateSdk("26", 26);
        var task = new ResolveSdk { BuildEngine = engine, SdkRoot = root };
        Assert.False(task.Execute());
        Assert.Contains("OpenHarmonyApiLevel", Assert.Single(engine.Errors).Message);
    }

    [Fact]
    public void BrokenToolchainFailsBeforeCompilation()
    {
        string native = CreateSdk("sdk/default/openharmony", 26);
        File.Delete(Path.Combine(native, "llvm/bin/clang.exe"));
        var task = new ResolveSdk { BuildEngine = engine, CommandLineTools = root };
        Assert.False(task.Execute());
        Assert.Contains("clang", Assert.Single(engine.Errors).Message);
    }

    [Theory]
    [InlineData("App.Run(); System.IO.File.Delete(\"file\")")]
    [InlineData("App.Run<int>")]
    [InlineData("Run")]
    public void EntryPointRejectsExpressions(string entryPoint)
    {
        string output = Path.Combine(root, "Entry.g.cs");
        var task = new GenerateEntryPoint { BuildEngine = engine, FilePath = output, EntryPoint = entryPoint };
        Assert.False(task.Execute());
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void SettingsPreservePathsAndSpecialCharactersWithoutShellInterpolation()
    {
        string path = Path.Combine(root, "settings.json");
        const string value = "C:\\Program Files\\SDK & tools\\中文;$(untouched)";
        var item = new TaskItem("CommandLineTools");
        item.SetMetadata("Value", value);
        Assert.True(new WriteSettings { BuildEngine = engine, FilePath = path, Settings = [item] }.Execute());
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(value, json.RootElement.GetProperty("CommandLineTools").GetString());
    }

    private string CreateSdk(string folder, int api)
    {
        string native = Path.GetFullPath(Path.Combine(root, folder, "native"));
        Directory.CreateDirectory(Path.Combine(native, "sysroot"));
        Directory.CreateDirectory(Path.Combine(native, "llvm/bin"));
        Directory.CreateDirectory(Path.Combine(native, "build/cmake"));
        foreach (string file in new[] { "llvm/bin/clang.exe", "llvm/bin/ld.lld.exe", "build/cmake/ohos.toolchain.cmake" })
            File.WriteAllText(Path.Combine(native, file), "");
        File.WriteAllText(Path.Combine(native, "oh-uni-package.json"), JsonSerializer.Serialize(new { apiVersion = api }));
        return native;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class TestBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";
        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => throw new NotSupportedException();
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }
    }
}
