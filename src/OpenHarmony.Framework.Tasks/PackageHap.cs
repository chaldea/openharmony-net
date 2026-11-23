using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;

namespace OpenHarmony.Framework.Tasks;

public sealed class PackageHap : HapTask
{
    [Required] public string StageTemplate { get; set; } = "";
    [Required] public string BuildMode { get; set; } = "";

    protected override void ExecuteHap()
    {
        string stage = Path.Combine(WorkDirectory, "Stage");
        string template = FullPath(StageTemplate);
        RequireFile(Path.Combine(PublishDirectory, AssemblyName + ".so"), "NativeAOT output was not generated.");
        foreach (string file in Directory.EnumerateFiles(template, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(stage, Path.GetRelativePath(template, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase))
                File.Copy(file, destination, true);
            else
            {
                string text = File.ReadAllText(file).Replace("@BUNDLE_NAME@", BundleName)
                    .Replace("@ASSEMBLY_NAME@", AssemblyName).Replace("@ABI@", Abi);
                if (!File.Exists(destination) || File.ReadAllText(destination) != text)
                    File.WriteAllText(destination, text);
            }
        }
        string libs = Directory.CreateDirectory(Path.Combine(stage, "entry/libs", Abi)).FullName;
        foreach (string file in Directory.EnumerateFiles(PublishDirectory))
            if (Regex.IsMatch(Path.GetFileName(file), @"\.so(\.|$)"))
                File.Copy(file, Path.Combine(libs, Path.GetFileName(file)), true);

        RunLauncher("ohpm", stage, "install");
        RunLauncher("hvigorw", stage, "--mode", "module", "-p", "product=default", "-p", "module=entry@default",
            "-p", $"buildMode={BuildMode}", "assembleHap", "--no-daemon");
        string hap = Path.Combine(stage, "entry/build/default/outputs/default/entry-default-unsigned.hap");
        RequireFile(hap, "Hvigor did not generate a HAP.");
        Directory.CreateDirectory(Path.GetDirectoryName(HapPath)!);
        File.Copy(hap, HapPath, true);
        Log.LogMessage(MessageImportance.High, "OpenHarmony HAP: {0}", HapPath);
    }

    private void RunLauncher(string name, string stage, params string[] arguments)
    {
        string launcher = Path.Combine(CommandLineTools, "bin", OperatingSystem.IsWindows() ? name + ".bat" : name);
        RequireFile(launcher, "Install the official HarmonyOS command-line tools.");
        var start = new ProcessStartInfo(launcher, arguments) { WorkingDirectory = stage };
        if (OperatingSystem.IsWindows())
        {
            // cmd runs the vendor's batch launchers. Keep the path out of the command
            // text and allow only literal arguments when passing through the batch file.
            if (arguments.Any(arg => !Regex.IsMatch(arg, @"\A[A-Za-z0-9_@=.-]+\z")))
                throw new ArgumentException($"Invalid Hvigor build mode '{BuildMode}'.");
            start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
            {
                Arguments = "/d /v:off /s /c \"\"%OHOS_LAUNCHER%\" " + string.Join(' ', arguments) + "\"",
                WorkingDirectory = stage
            };
            start.Environment["OHOS_LAUNCHER"] = launcher;
        }
        RunTool(start);
    }
}
