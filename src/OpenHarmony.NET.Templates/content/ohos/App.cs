public static class App
{
    // Invoked by the Stage application's N-API bridge. Return a value to display.
    public static int Run()
    {
        var values = new List<int> { 19, 23 };
        GC.Collect();
        return values.Sum();
    }
}
