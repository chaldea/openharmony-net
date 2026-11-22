namespace OpenHarmony;

public sealed class PlatformException(string operation, int nativeCode, string? detail = null)
    : Exception($"{operation} failed ({nativeCode}). {detail}".TrimEnd())
{
    public string Operation { get; } = operation;
    public int NativeCode { get; } = nativeCode;
    internal static void Check(int result, string operation)
    {
        if (result != 0) throw new PlatformException(operation, result);
    }
}
