using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using OpenHarmony.SdkResolution;

namespace OpenHarmony.NET.Sdk;

public sealed class ResolveSdk : Microsoft.Build.Utilities.Task
{
    public string SdkRoot { get; set; } = "";
    public string CommandLineTools { get; set; } = "";
    public int ApiLevel { get; set; }
    [Output] public string NativeRoot { get; set; } = "";
    [Output] public string ManifestPath { get; set; } = "";
    [Output] public string ManifestSha256 { get; set; } = "";
    [Output] public string Folder { get; set; } = "";
    [Output] public int ResolvedApiLevel { get; set; }

    public override bool Execute()
    {
        try
        {
            // The official CLI layout groups components under sdk/default/openharmony.
            string root = SdkRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                if (string.IsNullOrWhiteSpace(CommandLineTools))
                    throw new InvalidOperationException("Install the HarmonyOS SDK and set OpenHarmonyCommandLineTools (or OHOS_COMMAND_LINE_TOOLS), or OpenHarmonySdkRoot (OHOS_SDK_ROOT).");
                root = Path.Combine(CommandLineTools, "sdk", "default");
            }
            root = Path.GetFullPath(root);
            if (File.Exists(Path.Combine(root, "native", "oh-uni-package.json")))
                root = Directory.GetParent(root)!.FullName;
            if (ApiLevel == 0)
            {
                var manifests = Directory.EnumerateDirectories(root)
                    .Select(dir => Path.Combine(dir, "native", "oh-uni-package.json"))
                    .Where(File.Exists).ToArray();
                if (manifests.Length != 1)
                    throw new InvalidOperationException("Set OpenHarmonyApiLevel when the SDK root does not contain exactly one Native SDK.");
                using var json = JsonDocument.Parse(File.ReadAllText(manifests[0]));
                ApiLevel = int.Parse(json.RootElement.GetProperty("apiVersion").ToString(), System.Globalization.CultureInfo.InvariantCulture);
            }
            var sdk = OpenHarmonySdkResolver.Resolve(root, ApiLevel);
            NativeRoot = sdk.NativeRoot;
            ManifestPath = sdk.ManifestPath;
            ManifestSha256 = sdk.ManifestSha256;
            Folder = sdk.FolderName;
            ResolvedApiLevel = sdk.ApiLevel;
            Log.LogMessage(MessageImportance.High, "OpenHarmony Native SDK: API {0}, {1}", ApiLevel, NativeRoot);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or JsonException or FormatException)
        {
            Log.LogError(null, "OHOS1001", null, null, 0, 0, 0, 0, ex.Message);
            return false;
        }
    }
}

public sealed class WriteRunResponseFile : Microsoft.Build.Utilities.Task
{
    [Required] public string FilePath { get; set; } = "";
    [Required] public string Configuration { get; set; } = "";
    [Required] public string TargetFramework { get; set; } = "";

    public override bool Execute()
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in ((IBuildEngine6)BuildEngine).GetGlobalProperties())
            properties[property.Key] = property.Value;
        properties["Configuration"] = Configuration;
        properties["TargetFramework"] = TargetFramework;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        // Response files split on whitespace, and -property splits on commas/semicolons.
        // MSBuild unescapes the literal values after parsing these separators.
        File.WriteAllLines(FilePath, properties.Select(property => "-property:" + property.Key + "=" +
            Regex.Replace(property.Value, @"[%$@();,?*'""# \t\r\n]", m => $"%{(int)m.Value[0]:X2}")));
        return true;
    }
}

public sealed class WriteSettings : Microsoft.Build.Utilities.Task
{
    [Required] public string FilePath { get; set; } = "";
    [Required] public ITaskItem[] Settings { get; set; } = [];

    public override bool Execute()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(
            Settings.ToDictionary(item => item.ItemSpec, item => item.GetMetadata("Value")),
            new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }
}

public sealed class GenerateEntryPoint : Microsoft.Build.Utilities.Task
{
    [Required] public string FilePath { get; set; } = "";
    [Required] public string EntryPoint { get; set; } = "";
    public override bool Execute()
    {
        if (!Regex.IsMatch(EntryPoint, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$"))
        {
            Log.LogError("OpenHarmonyEntryPoint must name a static int method, for example App.Run.");
            return false;
        }
        string source = $$"""
            // Generated by OpenHarmony.NET.Sdk.
            internal static class OpenHarmonyNativeEntryPoint
            {
                [System.Runtime.InteropServices.UnmanagedCallersOnly(EntryPoint = "openharmony_main")]
                public static int Run()
                {
                    try { return global::{{EntryPoint}}(); }
                    catch (System.Exception exception) { System.Console.Error.WriteLine(exception); return -1; }
                }
                [System.Runtime.InteropServices.UnmanagedCallersOnly(EntryPoint = "openharmony_runtime_major")]
                public static int RuntimeMajor() => System.Environment.Version.Major;
            }
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        if (!File.Exists(FilePath) || File.ReadAllText(FilePath) != source)
            File.WriteAllText(FilePath, source);
        return true;
    }
}
