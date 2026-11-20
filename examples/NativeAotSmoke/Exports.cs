using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public static class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "openharmony_dotnet_add", CallConvs = [typeof(CallConvCdecl)])]
    public static int Add(int left, int right)
    {
        // HarmonyOS named mutexes coordinate threads within this app process.
        using var mutex = new Mutex(false, "openharmony-nativeaot-smoke");
        if (!mutex.WaitOne(TimeSpan.FromSeconds(5))) return -1;
        try
        {
            // Exercise managed allocation, LINQ, and GC before crossing back to ArkTS.
            var values = new List<int> { left, right };
            int result = values.Sum();
            GC.Collect();
            return result;
        }
        finally { mutex.ReleaseMutex(); }
    }

    [UnmanagedCallersOnly(EntryPoint = "openharmony_dotnet_version", CallConvs = [typeof(CallConvCdecl)])]
    public static int RuntimeMajor() => Environment.Version.Major;
}
