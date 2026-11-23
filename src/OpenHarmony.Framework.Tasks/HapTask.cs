using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;

namespace OpenHarmony.Framework.Tasks;

public abstract class HapTask : Microsoft.Build.Utilities.Task
{
    [Required] public string ProjectDirectory { get; set; } = "";
    [Required] public string AssemblyName { get; set; } = "";
    [Required] public string BundleName { get; set; } = "";
    [Required] public string Abi { get; set; } = "";
    [Required] public string WorkDirectory { get; set; } = "";
    [Required] public string PublishDirectory { get; set; } = "";
    [Required] public string HapPath { get; set; } = "";
    public string CommandLineTools { get; set; } = "";
    public string JavaHome { get; set; } = "";

    protected static string ExecutableExtension => OperatingSystem.IsWindows() ? ".exe" : "";
    protected string Java => Path.Combine(JavaHome, "bin", "java" + ExecutableExtension);
    protected string Toolchains => Path.Combine(CommandLineTools, "sdk/default/openharmony/toolchains");
    protected string FullPath(string path) => Path.GetFullPath(path, ProjectDirectory);

    public override bool Execute()
    {
        try
        {
            if (Abi is not ("x86_64" or "arm64-v8a"))
                throw new ArgumentException("OpenHarmonyAbi must be x86_64 or arm64-v8a.");
            if (!Regex.IsMatch(AssemblyName, @"\A[A-Za-z_][A-Za-z0-9_.-]*\z") ||
                !Regex.IsMatch(BundleName, @"\A[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+\z"))
                throw new ArgumentException("Invalid OpenHarmony assembly or bundle name.");
            if (string.IsNullOrWhiteSpace(CommandLineTools))
                throw new ArgumentException("Set OpenHarmonyCommandLineTools or OHOS_COMMAND_LINE_TOOLS to the official command-line-tools directory.");
            if (string.IsNullOrWhiteSpace(JavaHome))
                throw new ArgumentException("Set OpenHarmonyJavaHome or JAVA_HOME to a JDK 17+ directory.");
            CommandLineTools = FullPath(CommandLineTools);
            JavaHome = FullPath(JavaHome);
            PublishDirectory = FullPath(PublishDirectory);
            WorkDirectory = FullPath(WorkDirectory);
            HapPath = FullPath(HapPath);
            RequireFile(Java, "Set OpenHarmonyJavaHome or JAVA_HOME to a JDK 17+ directory.");
            ExecuteHap();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or InvalidOperationException or Win32Exception or System.Text.Json.JsonException)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    protected abstract void ExecuteHap();

    protected static void RequireFile(string path, string hint)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{hint} Missing file: {path}");
    }

    protected string RunTool(ProcessStartInfo start)
    {
        string node = Path.Combine(CommandLineTools, OperatingSystem.IsWindows() ? "tool/node" : "tool/node/bin");
        start.Environment["JAVA_HOME"] = JavaHome;
        start.Environment["DEVECO_SDK_HOME"] = Path.Combine(CommandLineTools, "sdk");
        start.Environment["PATH"] = string.Join(Path.PathSeparator, node, Path.Combine(JavaHome, "bin"), start.Environment["PATH"]);
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {start.FileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string output = stdout.GetAwaiter().GetResult().Trim();
        string error = stderr.GetAwaiter().GetResult().Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{start.FileName} exited with {process.ExitCode}.\n{output}\n{error}");
        if (output.Length > 0) Log.LogMessage(MessageImportance.Normal, "{0}", output);
        if (error.Length > 0) Log.LogMessage(MessageImportance.Normal, "{0}", error);
        return output;
    }
}
