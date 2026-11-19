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
            if (args.Length == 0 || args[0] is "--help" or "-h")
            {
                Console.WriteLine("""
                    harmonyos-workload init [--dotnet-root <directory>]
                    harmonyos-workload unregister [--dotnet-root <directory>]
                    harmonyos-workload --version

                    Registers the manifest embedded in this NuGet tool for a standalone
                    .NET SDK on Windows x64, Linux x64 or macOS x64/arm64.
                    Defaults to the SDK selected by dotnet on PATH,
                    honoring global.json. MSI-managed SDKs are not supported.
                    Install packs afterward with: dotnet workload install harmonyos --skip-manifest-update
                    Before changing versions or unregistering: dotnet workload uninstall harmonyos
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
            if (args[0] is not ("init" or "unregister"))
                throw new ArgumentException("Expected init or unregister. See harmonyos-workload --help.");
            string? root = null;
            if (args.Length != 1)
            {
                if (args.Length != 3 || args[1] != "--dotnet-root")
                    throw new ArgumentException("Only --dotnet-root <directory> is accepted.");
                root = Path.GetFullPath(args[2]);
            }
            bool supported = (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) && RuntimeInformation.ProcessArchitecture == Architecture.X64
                || OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;
            if (!supported)
                throw new InvalidOperationException("This workload requires Windows x64, Linux x64 or macOS x64/arm64.");
            string host = root is null ? "dotnet" : Path.Combine(root, SdkInstallation.DotnetFileName);
            string version = Run(host, ["--version"]);
            SdkInstallation sdk = SdkInstallation.Discover(version, Run(host, ["--list-sdks"]), root);
            var registration = new ManifestRegistration(sdk, metadata["WorkloadFeatureBand"]);
            if (args[0] == "unregister")
            {
                registration.Unregister();
                Console.WriteLine($"Unregistered harmonyos from {sdk.Root} ({sdk.FeatureBand}).");
            }
            else
            {
                string manifest = ReadResource("WorkloadManifest.json.in").Replace("@VERSION@", metadata["WorkloadVersion"]);
                registration.Register(manifest, ReadResource("WorkloadManifest.targets"),
                    () => Run(sdk.DotnetPath, ["workload", "config", "--update-mode", "manifests"]));
                Console.WriteLine($"Registered harmonyos {metadata["WorkloadVersion"]} for SDK {sdk.Version} at {sdk.Root}.");
                Console.WriteLine($"Use this SDK: \"{sdk.DotnetPath}\" workload install harmonyos --skip-manifest-update");
            }
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"harmonyos-workload: {ex.Message}");
            if (ex is UnauthorizedAccessException)
                Console.Error.WriteLine("Choose a writable standalone SDK with --dotnet-root, or run with permission to modify that SDK.");
            return 1;
        }
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
}
