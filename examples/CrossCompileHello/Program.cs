using System;
using System.Threading;

Console.WriteLine("Hello World");
// Exercise the Unix named-mutex implementation when validating a published runtime.
using var mutex = new Mutex(false, "openharmony-net11-hello");
if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
    throw new TimeoutException("Could not acquire the sample mutex.");
try
{
    Console.WriteLine($"CROSS_COMPILE_HELLO runtime={Environment.Version.Major} mutex=ok");
    if (Environment.Version.Major != 11)
        throw new InvalidOperationException("This sample requires .NET 11.");
}
finally { mutex.ReleaseMutex(); }
