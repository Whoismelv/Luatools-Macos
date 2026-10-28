using System.Runtime.InteropServices;

namespace LuaTools.Core;

public static class SteamLocator
{
    public static SteamInstall? Locate(string? overrideRoot = null)
    {
        var root = overrideRoot ?? FindRoot();
        if (root is null || !Directory.Exists(root)) return null;

        var steamApps = Path.Combine(root, "steamapps");
        if (!Directory.Exists(steamApps))
        {
            // Some layouts put manifests directly under the root on Linux.
            steamApps = root;
        }

        var libraries = new List<SteamLibrary>
        {
            new(root, SafeAppIds(Path.Combine(root, "steamapps")))
        };

        var lf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(lf))
        {
            try
            {
                var parsed = Vdf.ParseFile(lf);
                var folders = Vdf.Map(parsed, "libraryfolders")
                              ?? Vdf.Map(parsed, "LibraryFolders")
                              ?? parsed;

                foreach (var (key, value) in folders)
                {
                    if (value is Dictionary<string, object> entry)
                    {
                        var p = Vdf.Str(entry, "path");
                        if (string.IsNullOrWhiteSpace(p)) continue;
                        var normalized = p.Replace("\\\\", "\\");
                        if (libraries.Any(l => PathsEqual(l.Path, normalized))) continue;
                        libraries.Add(new SteamLibrary(normalized, SafeAppIds(Path.Combine(normalized, "steamapps"))));
                    }
                    else if (value is string rawPath &&
                             !key.Equals("contentstatsid", StringComparison.OrdinalIgnoreCase) &&
                             long.TryParse(key, out _))
                    {
                        if (libraries.Any(l => PathsEqual(l.Path, rawPath))) continue;
                        libraries.Add(new SteamLibrary(rawPath, SafeAppIds(Path.Combine(rawPath, "steamapps"))));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"libraryfolders.vdf unreadable: {ex.Message}");
            }
        }

        return new SteamInstall(root, steamApps, libraries.Where(l => Directory.Exists(l.Path)).ToList());
    }

    private static string? FindRoot()
    {
        if (PlatformPaths.IsWindows)
        {
            foreach (var candidate in WindowsCandidates())
                if (Directory.Exists(Path.Combine(candidate, "steamapps"))) return candidate;
            return null;
        }

        if (PlatformPaths.IsMac)
        {
            var userSteam = Path.Combine(PlatformPaths.Home, "Library", "Application Support", "Steam");
            if (Directory.Exists(Path.Combine(userSteam, "steamapps"))) return userSteam;

            foreach (var v in new[] { "~/Steam", "/Applications/Steam.app/Contents/MacOS" })
            {
                var expanded = Expand(v);
                if (Directory.Exists(Path.Combine(expanded, "steamapps"))) return expanded;
            }
            return null;
        }

        foreach (var v in new[]
                 {
                     "~/.steam/steam", "~/.local/share/Steam", "~/.var/app/com.valvesoftware.Steam/.local/share/Steam"
                 })
        {
            var expanded = Expand(v);
            if (Directory.Exists(Path.Combine(expanded, "steamapps"))) return expanded;
        }
        return null;
    }

    private static IEnumerable<string> WindowsCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var hive in new[] { @"SOFTWARE\Valve\Steam", @"SOFTWARE\WOW6432Node\Valve\Steam" })
            {
                var path = RegistryRead(hive, "SteamPath") ?? RegistryRead(hive, "InstallPath");
                if (!string.IsNullOrWhiteSpace(path)) yield return path;
            }
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            yield return Path.Combine(drive.RootDirectory.FullName, "SteamLibrary");
            yield return Path.Combine(drive.RootDirectory.FullName, "Steam");
            yield return Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "Steam");
        }
    }

    private static string? RegistryRead(string key, string name)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
            return k?.GetValue(name) as string;
        }
        catch { return null; }
    }

    private static string Expand(string path) =>
        path.StartsWith('~') ? Path.Combine(PlatformPaths.Home, path[1..].TrimStart('/', '\\')) : path;

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'),
            Path.GetFullPath(b).TrimEnd('/', '\\'),
            PlatformPaths.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static IReadOnlyList<long> SafeAppIds(string steamAppsDir)
    {
        if (!Directory.Exists(steamAppsDir)) return [];
        try
        {
            return Directory.EnumerateFiles(steamAppsDir, "appmanifest_*.acf")
                .Select(f =>
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    return long.TryParse(name.AsSpan("appmanifest_".Length), out var id) ? id : 0;
                })
                .Where(id => id > 0)
                .ToArray();
        }
        catch { return []; }
    }

    public static AppManifestFile? ReadManifest(string acfPath)
    {
        if (!File.Exists(acfPath)) return null;
        try
        {
            var root = Vdf.ParseFile(acfPath);
            var state = Vdf.Map(root, "AppState") ?? root;

            var depots = Vdf.Map(state, "InstalledDepots");
            var depotIds = depots is null
                ? []
                : depots.Keys.Select(k => long.TryParse(k, out var d) ? d : 0).Where(d => d > 0).ToArray();

            return new AppManifestFile(
                ParseLong(Vdf.Str(state, "appid")),
                Vdf.Str(state, "name") ?? "Unknown",
                Vdf.Str(state, "installdir") ?? "",
                Vdf.Str(state, "StateFlags") ?? "0",
                ParseLong(Vdf.Str(state, "SizeOnDisk")),
                ParseLong(Vdf.Str(state, "buildid")),
                ParseLong(Vdf.Str(state, "LastUpdated")),
                depotIds,
                acfPath);
        }
        catch (Exception ex)
        {
            Log.Warn($"manifest read failed {acfPath}: {ex.Message}");
            return null;
        }
    }

    public static IReadOnlyList<AppManifestFile> EnumerateInstalled(SteamInstall install) =>
        install.Libraries
            .SelectMany(l => l.AppIds)
            .Distinct()
            .Select(id => ReadManifest(Path.Combine(install.SteamAppsDir, $"appmanifest_{id}.acf"))
                          ?? install.Libraries
                              .Select(l => Path.Combine(l.SteamAppsDir, $"appmanifest_{id}.acf"))
                              .Where(File.Exists)
                              .Select(ReadManifest)
                              .FirstOrDefault(m => m is not null))
            .Where(m => m is not null)
            .Select(m => m!)
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static long ParseLong(string? s) => long.TryParse(s, out var v) ? v : 0;
}
