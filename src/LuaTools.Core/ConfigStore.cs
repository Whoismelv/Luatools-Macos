using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuaTools.Core;

public sealed class AppConfig
{
    public string? SteamPathOverride { get; set; }
    public string Language { get; set; } = "english";
    public int MaxConcurrentDownloads { get; set; } = 8;
    public bool ValidateAfterDownload { get; set; } = false;
    public bool StartWithSystem { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoUpdateApps { get; set; } = true;
    public string Theme { get; set; } = "dark";
    public string DefaultInstallRoot { get; set; } = "";
    public bool ShowAdultContent { get; set; } = false;
    /// <summary>macos | windows | linux | auto. LuaTools on mac only ships macos depots by default.</summary>
    public string TargetPlatform { get; set; } = "macos";
}

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AppConfig Value { get; private set; } = new();

    public static ConfigStore Load()
    {
        var store = new ConfigStore();
        try
        {
            if (File.Exists(PlatformPaths.ConfigFile))
            {
                store.Value = JsonSerializer.Deserialize<AppConfig>(
                    File.ReadAllText(PlatformPaths.ConfigFile), Options) ?? new AppConfig();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"config unreadable, using defaults: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(store.Value.DefaultInstallRoot))
        {
            var steam = SteamLocator.Locate(store.Value.SteamPathOverride);
            store.Value.DefaultInstallRoot = steam is not null
                ? Path.Combine(steam.Libraries[0].CommonDir, "LuaTools")
                : Path.Combine(PlatformPaths.Home, "Games", "LuaTools");
        }

        return store;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(PlatformPaths.ConfigFile, JsonSerializer.Serialize(Value, Options));
        }
        catch (Exception ex)
        {
            Log.Error($"config save failed: {ex.Message}");
        }
    }
}
