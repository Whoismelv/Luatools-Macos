using Avalonia;

namespace LuaTools.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--selftest")) return SelfTest.Run();
        if (args.Contains("--verify-tools")) return ToolCheck.RunAsync().GetAwaiter().GetResult();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
