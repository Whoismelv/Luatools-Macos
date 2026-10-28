using System.Runtime.InteropServices;

namespace LuaTools.Core;

public static class PlatformPaths
{
    public static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static string Home =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string AppDataDir
    {
        get
        {
            var dir = IsMac
                ? Path.Combine(Home, "Library", "Application Support", "LuaToolsMac")
                : IsWindows
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LuaToolsMac")
                    : Path.Combine(
                        Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                        ?? Path.Combine(Home, ".local", "share"), "luatools-mac");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string CacheDir => Ensure(Path.Combine(AppDataDir, "cache"));
    public static string ToolsDir => Ensure(Path.Combine(AppDataDir, "tools"));
    public static string LetsDir => Ensure(Path.Combine(AppDataDir, "fixes"));
    public static string LogsDir => Ensure(Path.Combine(AppDataDir, "logs"));

    public static string ConfigFile => Path.Combine(AppDataDir, "config.json");
    public static string AuthFile => Path.Combine(AppDataDir, "auth.dat");
    public static string AppliedFixesFile => Path.Combine(AppDataDir, "applied-fixes.json");

    public static string ExecutableExtension => IsWindows ? ".exe" : "";

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
