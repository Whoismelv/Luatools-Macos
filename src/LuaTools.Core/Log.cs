namespace LuaTools.Core;

public static class Log
{
    private static readonly object Gate = new();

    public static string FilePath =>
        Path.Combine(PlatformPaths.LogsDir, $"luatools-{DateTime.Now:yyyy-MM-dd}.log");

    public static event Action<string>? Message;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERR ", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
        Message?.Invoke(line);
        try
        {
            lock (Gate)
                System.IO.File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch { }
    }
}
