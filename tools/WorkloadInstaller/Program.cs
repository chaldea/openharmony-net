using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Chaldea.OpenHarmony.Workload;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "emulator")
                return EmulatorCommands.Execute(args[1..]);

            if (args.Length == 0 || args is ["workload"] || args.Any(arg => arg is "--help" or "-h" or "-?"))
            {
                Console.WriteLine("""
                    ohos emulator list [--instance-path <directory>]
                    ohos emulator images [--device-type <type>] [--image-root <directory>] [--http-proxy <url>]
                    ohos emulator create [name] [--device-type <type>] [--os-version <version>] [options]
                    ohos emulator run [name] [--command-line-tools <directory>] [--instance-path <directory>] [--image-root <directory>]
                    ohos emulator delete <name> [--command-line-tools <directory>] [--instance-path <directory>]
                    ohos emulator stop [name] [--command-line-tools <directory>]
                    ohos workload register [--dotnet-root <directory>]
                    ohos workload unregister [--dotnet-root <directory>]
                    ohos workload install [dotnet workload install options]
                    ohos workload list [dotnet workload list options]
                    ohos workload update [dotnet workload update options]
                    ohos workload uninstall [dotnet workload uninstall options]
                    ohos --version

                    Select the SDK with --dotnet-root <directory>, DOTNET_ROOT or dotnet on PATH.
                    SDK selection honors global.json. Install and update accept --source <feed>.
                    Set OHOS_COMMAND_LINE_TOOLS or use --command-line-tools to select DevEco command-line tools.
                    """);
                return 0;
            }
            var metadata = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .ToDictionary(item => item.Key, item => item.Value!);
            if (args is ["--version"])
            {
                Console.WriteLine(metadata["WorkloadVersion"]);
                return 0;
            }
            if (args is ["sdk", "install", ..])
                throw new InvalidOperationException("SDK installation is not supported yet.");
            if (args.Length < 2 || args[0] != "workload")
                throw new ArgumentException("Expected a workload command. See ohos --help.");

            string command = args[1];
            if (command is not ("register" or "unregister" or "install" or "list" or "update" or "uninstall"))
                throw new ArgumentException($"Unknown workload command '{args[1]}'. See ohos --help.");

            var options = args[2..].ToList();
            string? root = TakeDotnetRoot(options);
            if (command is "register" or "unregister" && options.Count > 0)
                throw new ArgumentException($"The workload {command} command does not accept additional arguments.");

            bool supported = (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) && RuntimeInformation.ProcessArchitecture == Architecture.X64
                || OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;
            if (!supported)
                throw new InvalidOperationException("This workload requires Windows x64, Linux x64 or macOS x64/arm64.");

            string band = metadata["WorkloadFeatureBand"];
            var sdk = DiscoverSdk(root, band);
            var registration = new ManifestRegistration(sdk, band);
            switch (command)
            {
                case "register":
                case "install":
                    Register(sdk, registration, metadata["WorkloadVersion"]);
                    if (command == "register") return 0;
                    // Prefer the embedded pack versions unless the caller chose a manifest policy.
                    if (!options.Any(arg => arg.Split('=', ':')[0] is "--skip-manifest-update" or "--version" or "--from-rollback-file"))
                        options.Insert(0, "--skip-manifest-update");
                    return RunInteractive(sdk.DotnetPath, ["workload", "install", "harmonyos", .. options]);
                case "uninstall":
                    registration.ValidateSdk();
                    // Idle Windows MSBuild nodes retain the workload task assembly after a build.
                    if (OperatingSystem.IsWindows())
                        Run(sdk.DotnetPath, ["build-server", "shutdown", "--msbuild"]);
                    int code = RunInteractive(sdk.DotnetPath, ["workload", "uninstall", "harmonyos", .. options]);
                    if (code != 0) return code;
                    goto case "unregister";
                case "unregister":
                    registration.Unregister();
                    Console.WriteLine($"Unregistered harmonyos from {sdk.Root} ({sdk.FeatureBand}).");
                    return 0;
                default: // list and update apply to all installed workloads.
                    return RunInteractive(sdk.DotnetPath, ["workload", command, .. options]);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"ohos: {ex.Message}");
            if (ex is UnauthorizedAccessException)
                Console.Error.WriteLine("Select a writable standalone SDK with DOTNET_ROOT or --dotnet-root, or run with permission to modify that SDK.");
            return 1;
        }
    }

    private static string? TakeDotnetRoot(List<string> options)
    {
        int index = options.IndexOf("--dotnet-root");
        if (index < 0) return Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (index + 1 == options.Count || string.IsNullOrWhiteSpace(options[index + 1]) || options[index + 1].StartsWith('-')
            || options.LastIndexOf("--dotnet-root") != index)
            throw new ArgumentException("--dotnet-root must be followed by one SDK directory.");
        string root = options[index + 1];
        options.RemoveRange(index, 2);
        return root;
    }

    private static SdkInstallation DiscoverSdk(string? root, string band)
    {
        root = string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);
        string host = root == null ? "dotnet" : Path.Combine(root, SdkInstallation.DotnetFileName);
        try
        {
            var sdk = SdkInstallation.Discover(Run(host, ["--version"]), Run(host, ["--list-sdks"]), root);
            if (sdk.FeatureBand != band)
                throw new InvalidOperationException($"Selected SDK {sdk.Version} belongs to feature band {sdk.FeatureBand}.");
            return sdk;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException($"{ex.Message}\nInstall .NET SDK feature band {band} or select it with DOTNET_ROOT or --dotnet-root.", ex);
        }
    }

    private static void Register(SdkInstallation sdk, ManifestRegistration registration, string version)
    {
        string manifest = ReadResource("WorkloadManifest.json.in").Replace("@VERSION@", version);
        registration.Register(manifest, ReadResource("WorkloadManifest.targets"),
            () => Run(sdk.DotnetPath, ["workload", "config", "--update-mode", "manifests"]));
        Console.WriteLine($"Registered harmonyos {version} for SDK {sdk.Version} at {sdk.Root}.");
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Tool resource missing: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Run(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {executable}.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"Timed out running {executable} {string.Join(' ', arguments)}.");
        }
        Task.WaitAll(output, error);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{executable} {string.Join(' ', arguments)} failed: {error.Result.Trim()} {output.Result.Trim()}");
        return output.Result.Trim();
    }

    private static int RunInteractive(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {executable}.");
        process.WaitForExit();
        return process.ExitCode;
    }
}
