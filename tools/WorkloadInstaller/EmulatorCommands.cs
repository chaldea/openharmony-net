using System.Diagnostics;
using System.Text.Json;

namespace Chaldea.OpenHarmony.Workload;

internal static class EmulatorCommands
{
    internal delegate (int ExitCode, string Output) InvokeEmulator(string[] arguments, bool captureOutput = false);
    private const string DefaultName = "MyPhone";
    private const string DefaultVersion = "HarmonyOS 6.1.0(23)";
    private static string UserHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string HuaweiRoot => Path.Combine(UserHome, ".Huawei");
    private static string DefaultCommandLineTools => Path.Combine(HuaweiRoot, "command-line-tools");
    private static string DefaultInstancePath => Path.Combine(HuaweiRoot, "Emulator", "deployed");

    private static string[] ImageRoots(Dictionary<string, string> options)
    {
        if (options.TryGetValue("--image-root", out string? root)) return [Path.GetFullPath(root)];
        string localSdk = Path.Combine(HuaweiRoot, "Sdk");
        if (OperatingSystem.IsWindows())
            return [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Huawei", "Sdk"), localSdk];
        if (OperatingSystem.IsMacOS())
            return [Path.Combine(UserHome, "Library", "Huawei", "Sdk"), localSdk];
        return [localSdk];
    }

    public static int Execute(string[] args)
    {
        if (args.Length == 0 || args.Any(arg => arg is "--help" or "-h" or "-?"))
        {
            PrintUsage();
            return 0;
        }

        string action = args[0];
        bool hasName = args.Length > 1 && !args[1].StartsWith("--");
        string name = hasName ? args[1] : DefaultName;
        ValidateName(name, "emulator");
        var options = ParseOptions(args[(hasName ? 2 : 1)..]);
        return action switch
        {
            "create" => Create(name, options, Command(ResolveEmulator(options))),
            "run" => Run(name, options),
            "stop" => Stop(name, options),
            _ => throw new ArgumentException($"Unknown emulator command '{action}'. See ohos --help.")
        };
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
        {
            string key = args[index];
            if (key is not ("--device-type" or "--os-version" or "--command-line-tools" or "--instance-path" or "--image-root" or "--http-proxy"))
                throw new ArgumentException($"Unknown emulator option '{key}'. See ohos --help.");
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith('-'))
                throw new ArgumentException($"{key} must be followed by a value.");
            if (!options.TryAdd(key, args[index + 1]))
                throw new ArgumentException($"{key} can only be specified once.");
        }
        return options;
    }

    internal static int Create(string name, Dictionary<string, string> options, InvokeEmulator invoke)
    {
        string deviceType = options.GetValueOrDefault("--device-type", "phone");
        string? requestedVersion = options.GetValueOrDefault("--os-version");
        string instancePath = GetOption(options, "--instance-path", DefaultInstancePath);
        string[] imageRoots = ImageRoots(options);
        string downloadRoot = imageRoots[^1];
        Directory.CreateDirectory(downloadRoot);
        Directory.CreateDirectory(instancePath);
        // Different instances can share one download. Keep concurrent installs out of the same cache.
        using var cacheLock = new FileStream(Path.Combine(downloadRoot, ".ohos-create.lock"), FileMode.OpenOrCreate,
            FileAccess.Write, FileShare.None);
        string device = Path.Combine(instancePath, name);
        string descriptor = device + ".ini";
        if (Directory.Exists(device) || File.Exists(descriptor))
            throw new InvalidOperationException($"Device '{name}' already exists in {instancePath}. Use 'ohos emulator run {name}' or choose another name.");

        string[] proxy = options.TryGetValue("--http-proxy", out string? url) ? ["-http_proxy", url] : [];
        string osVersion = requestedVersion ?? DefaultVersion;
        bool Created() => File.Exists(descriptor) && File.Exists(Path.Combine(device, "config.ini"));
        string[] CreateArguments(string imageRoot) => ["-create", name, "-deviceType", deviceType, "-osVersion", osVersion,
            "-instancePath", instancePath, "-imageRoot", imageRoot];
        bool TryCreateCached(string imageRoot)
        {
            if (!InstalledImages(imageRoot).Any()) return false;
            // A successful native create verifies the requested version and ABI without a network lookup.
            var (code, message) = invoke(CreateArguments(imageRoot), captureOutput: true);
            if (code == 0 && Created())
            {
                Console.WriteLine($"Created emulator '{name}' using the installed image in {imageRoot}.");
                return true;
            }
            if (Directory.Exists(device) || File.Exists(descriptor))
                throw new InvalidOperationException($"Emulator left an incomplete device at {device}. {message}".TrimEnd());
            if (message.Length > 0 && !message.Contains("find image", StringComparison.OrdinalIgnoreCase)
                && !message.Contains("verify your SDK installation", StringComparison.OrdinalIgnoreCase)
                && !message.Equals("Device create fail.", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Emulator could not create '{name}': {message}");
            return false;
        }
        if (imageRoots.Any(TryCreateCached)) return 0;
        if (requestedVersion == null)
        {
            osVersion = SelectDefaultVersion(invoke, deviceType, downloadRoot, proxy);
            if (osVersion != DefaultVersion && imageRoots.Any(TryCreateCached)) return 0;
        }
        int installCode = DownloadImage(invoke, deviceType, osVersion, downloadRoot, proxy);
        if (installCode != 0) return installCode;
        int result = invoke(CreateArguments(downloadRoot)).ExitCode;
        if (result != 0 || !Created())
        {
            Console.Error.WriteLine($"Emulator could not create '{name}' in {instancePath} with the downloaded {osVersion} image in {downloadRoot} (exit code {result}).");
            return result != 0 ? result : 1;
        }
        Console.WriteLine($"Created emulator '{name}' ({deviceType}, {osVersion}).");
        return 0;
    }

    private static string SelectDefaultVersion(InvokeEmulator invoke, string deviceType, string imageRoot, string[] proxy)
    {
        var (code, output) = invoke(["-imageList", "-deviceType", deviceType, "-imageRoot", imageRoot, .. proxy], captureOutput: true);
        if (code != 0 || !output.StartsWith('['))
            throw new InvalidOperationException($"Could not read the Emulator image catalog for {deviceType}. Check your network or --http-proxy. {output}".TrimEnd());
        var catalog = JsonSerializer.Deserialize<CatalogImage[]>(output, JsonSerializerOptions.Web) ?? [];
        // The official catalog lists images newest first. Preserve the documented default where available.
        return catalog.Where(image => string.Equals(image?.DeviceType, deviceType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(image => image.OsVersion == DefaultVersion)
            .ThenByDescending(image => string.Equals(image.ReleaseType, "Release", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(image => string.Equals(image.Downloaded.ToString(), "true", StringComparison.OrdinalIgnoreCase))
            .Select(image => image.OsVersion)
            .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version))
            ?? throw new InvalidOperationException($"No {deviceType} image is available in the Emulator catalog for this host.");
    }

    private sealed record CatalogImage(string? DeviceType, string? OsVersion, string? ReleaseType, JsonElement Downloaded);

    private static IEnumerable<string> InstalledImages(string root)
    {
        string images = Path.Combine(root, "system-image");
        if (!Directory.Exists(images)) yield break;
        foreach (string platform in Directory.EnumerateDirectories(images))
        {
            foreach (string variant in Directory.EnumerateDirectories(platform))
            {
                if (HasSystemImage(variant))
                    yield return variant;
            }
        }
    }

    private static bool HasSystemImage(string directory) =>
        File.Exists(Path.Combine(directory, "sdk-pkg.json")) &&
        new FileInfo(Path.Combine(directory, "system.img")) is { Exists: true, Length: > 0 };

    private static int DownloadImage(InvokeEmulator invoke, string deviceType, string osVersion, string imageRoot, string[] proxy)
    {
        // An interrupted download in the target directory makes -install prompt for deletion.
        // A fresh root also lets us verify this particular install even when Emulator returns 0 on failure.
        string staging = Directory.CreateDirectory(Path.Combine(imageRoot, $".download-{Guid.NewGuid():N}")).FullName;
        string previous = Path.Combine(staging, "previous");
        bool installed = false;
        try
        {
            Console.WriteLine($"Downloading {deviceType} image {osVersion} to {imageRoot}.");
            int code = invoke(["-install", "-deviceType", deviceType, "-osVersion", osVersion,
                "-imageRoot", staging, .. proxy]).ExitCode;
            if (code != 0)
            {
                Console.Error.WriteLine($"Image download failed for {deviceType}, {osVersion} (exit code {code}).");
                return code;
            }
            string[] images = InstalledImages(staging).ToArray();
            if (images.Length != 1)
                throw new InvalidOperationException($"Emulator did not install a system image for {deviceType}, {osVersion}. "
                    + $"Check the image version with 'Emulator -imageList -deviceType {deviceType}' and the download diagnostics above.");

            string target = Path.Combine(imageRoot, Path.GetRelativePath(staging, images[0]));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (Directory.Exists(target)) Directory.Move(target, previous);
            try
            {
                Directory.Move(images[0], target);
                installed = true;
            }
            catch
            {
                if (Directory.Exists(previous)) Directory.Move(previous, target);
                throw;
            }
            Console.WriteLine($"Installed {deviceType} image {osVersion} in {target}.");
            return 0;
        }
        finally
        {
            // If restoring the original cache also failed, retain its backup for recovery.
            if (installed || !Directory.Exists(previous)) Directory.Delete(staging, recursive: true);
        }
    }

    private static InvokeEmulator Command(string executable) => (arguments, captureOutput) =>
    {
        using var process = StartProcess(executable, arguments, captureOutput);
        var stdout = captureOutput ? process.StandardOutput.ReadToEndAsync() : null;
        var stderr = captureOutput ? process.StandardError.ReadToEndAsync() : null;
        process.WaitForExit();
        return (process.ExitCode, string.Join(Environment.NewLine, stdout?.GetAwaiter().GetResult(), stderr?.GetAwaiter().GetResult()).Trim());
    };

    private static int Run(string name, Dictionary<string, string> options)
    {
        RejectOptions(options, "--device-type", "--os-version", "--http-proxy");
        string instancePath = GetOption(options, "--instance-path", DefaultInstancePath);
        string config = Path.Combine(instancePath, name, "config.ini");
        if (!File.Exists(config))
            throw new FileNotFoundException($"Cannot find device '{name}' in {instancePath}. Create it with 'ohos emulator create {name}'.");
        string[] imageRoots = ImageRoots(options);
        string imageRoot = imageRoots[^1];
        if (imageRoots.Length > 1)
        {
            const string prefix = "imageSubPath=";
            string? subPath = File.ReadLines(config).FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
            if (!string.IsNullOrWhiteSpace(subPath) && !Path.IsPathRooted(subPath))
                imageRoot = imageRoots.FirstOrDefault(root => HasSystemImage(Path.Combine(root, subPath))) ?? imageRoot;
        }
        string executable = ResolveEmulator(options);
        using Process emulator = StartProcess(executable, ["-start", name, "-instancePath", instancePath, "-imageRoot", imageRoot]);
        return 0;
    }

    private static int Stop(string name, Dictionary<string, string> options)
    {
        RejectOptions(options, "--device-type", "--os-version", "--instance-path", "--image-root", "--http-proxy");
        var invoke = Command(ResolveEmulator(options));
        return invoke(["-stop", name]).ExitCode;
    }

    private static string ResolveEmulator(Dictionary<string, string> options)
    {
        string? configured = options.GetValueOrDefault("--command-line-tools") ?? Environment.GetEnvironmentVariable("OHOS_COMMAND_LINE_TOOLS");
        if (!string.IsNullOrWhiteSpace(configured))
            return FindEmulator(Path.GetFullPath(configured)) ?? throw new FileNotFoundException($"No Command Line Tools Emulator in {configured}.");
        if (FindEmulator(DefaultCommandLineTools) is string emulator) return emulator;
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var bin = new DirectoryInfo(entry.Trim('"'));
            if (bin.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) && bin.Parent != null &&
                File.Exists(Path.Combine(bin.FullName, OperatingSystem.IsWindows() ? "hvigorw.bat" : "hvigorw")) &&
                FindEmulator(bin.Parent.FullName) is string installed) return installed;
        }
        throw new FileNotFoundException("Cannot find the HarmonyOS Emulator. Add command-line-tools/bin to PATH or pass --command-line-tools <directory>.");
    }

    private static string? FindEmulator(string toolsRoot)
    {
        string emulatorDirectory = Path.Combine(toolsRoot, "emulator");
        string[] candidates = OperatingSystem.IsWindows()
            ? [Path.Combine(emulatorDirectory, "Emulator.exe")]
            : [
                Path.Combine(toolsRoot, "bin", "Emulator"),
                Path.Combine(emulatorDirectory, "Emulator"),
                Path.Combine(emulatorDirectory, "emulator"),
                Path.Combine(emulatorDirectory, "Emulator.app", "Contents", "MacOS", "Emulator")
            ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static Process StartProcess(string executable, IEnumerable<string> arguments, bool captureOutput = false)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = captureOutput, RedirectStandardError = captureOutput };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {executable}.");
    }

    private static string GetOption(Dictionary<string, string> options, string key, string fallback)
    {
        return Path.GetFullPath(options.GetValueOrDefault(key, fallback));
    }

    private static void RejectOptions(Dictionary<string, string> options, params string[] unsupported)
    {
        string? option = options.Keys.FirstOrDefault(unsupported.Contains);
        if (option != null)
            throw new ArgumentException($"{option} is not supported for this emulator command.");
    }

    private static void ValidateName(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('-') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\') || name is "." or "..")
            throw new ArgumentException($"The {description} name must be a valid, non-empty device name.");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            ohos emulator create [name] [--device-type <type>] [--os-version <version>] [--http-proxy <url>]
                [--command-line-tools <directory>] [--instance-path <directory>] [--image-root <directory>]
            ohos emulator run [name] [--command-line-tools <directory>] [--instance-path <directory>] [--image-root <directory>]
            ohos emulator stop [name] [--command-line-tools <directory>]

            The default device name is MyPhone. Instances are stored in ~/.Huawei/Emulator/deployed.
            Create and run look for images in %LOCALAPPDATA%\Huawei\Sdk (Windows) or ~/Library/Huawei/Sdk (macOS) first.
            Missing images are downloaded to ~/.Huawei/Sdk. Use --image-root to select a different image directory.
            Create prefers the phone image "HarmonyOS 6.1.0(23)" and uses an available release if the host catalog omits it.
            List image versions with Emulator -imageList -deviceType phone.
            """);
    }
}
