using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public static class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "openharmony_dotnet_add", CallConvs = [typeof(CallConvCdecl)])]
    public static int Add(int left, int right)
    {
        // Exercise managed allocation, LINQ, and a collection before crossing back to ArkTS.
        var values = new List<int> { left, right };
        int result = values.Sum();
        GC.Collect();
        return result;
    }

    [UnmanagedCallersOnly(EntryPoint = "openharmony_dotnet_version", CallConvs = [typeof(CallConvCdecl)])]
    public static int RuntimeMajor() => Environment.Version.Major;
}
