using System.Runtime.InteropServices;
using LuaTools.Core;

namespace LuaTools.App;

/// <summary>
/// Headless verification. Runs the exact binary that ships, on the machine that
/// built it, without opening a window — so CI can prove the bundle executes on
/// macOS instead of only proving it compiled.
/// </summary>
internal static class SelfTest
{
    private static int _failed;

    public static int Run()
    {
        Console.WriteLine("luatools selftest");
        Console.WriteLine($"  os:      {RuntimeInformation.OSDescription}");
        Console.WriteLine($"  arch:    {RuntimeInformation.OSArchitecture}");
        Console.WriteLine($"  runtime: {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"  rid tag: {KnownTools.RuntimeTag}");
        Console.WriteLine($"  appdata: {PlatformPaths.AppDataDir}");
        Console.WriteLine();

        VdfParsesLibraryFolders();
        VdfParsesAppManifest();
        MacDepotFiltering();
        StorageIsWritable();
        RuntimeTagMatchesHost();
        LiveSteamProbe();

        Console.WriteLine();
        Console.WriteLine(_failed == 0 ? "SELFTEST OK" : $"SELFTEST FAILED ({_failed})");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {name}{(detail is null ? "" : " — " + detail)}");
        if (!ok) _failed++;
    }

    private static void VdfParsesLibraryFolders()
    {
        const string vdf = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"/Users/player/Library/Application Support/Steam"
            		"label"		""
            		"apps"
            		{
            			"440"		"1234567"
            			"570"		"7654321"
            		}
            	}
            	"1"
            	{
            		"path"		"/Volumes/Games/SteamLibrary"
            	}
            }
            """;

        var root = Vdf.Parse(vdf);
        var libs = Vdf.Map(root, "libraryfolders");

        Check("vdf: top-level key present", libs is not null);

        var zero = libs is null ? null : Vdf.Map(libs, "0");
        var path = zero is null ? null : Vdf.Str(zero, "path");
        Check("vdf: quoted path with spaces",
            path == "/Users/player/Library/Application Support/Steam", path);

        var apps = zero is null ? null : Vdf.Map(zero, "apps");
        Check("vdf: app id list", apps is not null && apps.Count == 2 && Vdf.Str(apps, "440") == "1234567");

        var one = libs is null ? null : Vdf.Map(libs, "1");
        Check("vdf: second library", one is not null && Vdf.Str(one, "path") == "/Volumes/Games/SteamLibrary");
    }

    private static void VdfParsesAppManifest()
    {
        const string acf = """
            "AppState"
            {
            	"appid"		"268910"
            	"name"		"Cuphead"
            	"StateFlags"		"4"
            	"installdir"		"Cuphead"
            	"SizeOnDisk"		"3584392491"
            	"buildid"		"9012345"
            	"LastUpdated"		"1700000000"
            	"InstalledDepots"
            	{
            		"268911"  { "manifest" "1234567890123456789" }
            	}
            }
            """;

        var root = Vdf.Parse(acf);
        var state = Vdf.Map(root, "AppState");
        Check("acf: AppState present", state is not null);
        Check("acf: appid", state is not null && Vdf.Str(state, "appid") == "268910");
        Check("acf: installdir", state is not null && Vdf.Str(state, "installdir") == "Cuphead");

        var depots = state is null ? null : Vdf.Map(state, "InstalledDepots");
        var depot = depots is null ? null : Vdf.Map(depots, "268911");
        Check("acf: nested depot manifest",
            depot is not null && Vdf.Str(depot, "manifest") == "1234567890123456789");
    }

    private static void MacDepotFiltering()
    {
        static DepotEntry Depot(params string[] oslist) =>
            new(100, 1, 0, "", false, null, oslist);

        Check("filter: explicit macos depot counts", Depot("macos").SupportsMac);
        Check("filter: windows depot does not", !Depot("windows").SupportsMac);
        Check("filter: shared depot is usable but not mac-specific",
            Depot().SupportsMac);

        var linuxOnly = new AppDefinition(1, "LinuxOnly", null, null, null,
            [Depot("linux")], [], null);
        var withMac = new AppDefinition(2, "WithMac", null, null, null,
            [Depot("windows"), Depot("macos")], [], null);
        var sharedOnly = new AppDefinition(3, "SharedOnly", null, null, null,
            [Depot()], [], null);
        var windowsOnly = new AppDefinition(4, "WindowsOnly", null, null, null,
            [Depot("windows")], [], null);

        Check("filter: linux-only app rejected", !linuxOnly.SupportsMac);
        Check("filter: app with mac depot accepted", withMac.SupportsMac);
        Check("filter: shared-only app rejected at app level", !sharedOnly.SupportsMac);
        Check("filter: windows-only app rejected", !windowsOnly.SupportsMac);
        Check("filter: MacDepots keeps the macos depot only",
            withMac.MacDepots.Count == 1 && withMac.MacDepots[0].OsList.SequenceEqual(["macos"]),
            $"{withMac.MacDepots.Count}");
    }

    private static void StorageIsWritable()
    {
        var probe = Path.Combine(PlatformPaths.AppDataDir, ".selftest");
        try
        {
            File.WriteAllText(probe, "ok");
            var back = File.ReadAllText(probe);
            File.Delete(probe);
            Check("storage: appdata writable", back == "ok", PlatformPaths.AppDataDir);
        }
        catch (Exception ex)
        {
            Check("storage: appdata writable", false, ex.GetType().Name);
        }
    }

    private static void RuntimeTagMatchesHost()
    {
        var expected = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "macos-arm64" : "macos-x64")
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "windows-arm64" : "windows-x64")
                : (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64");

        Check("tools: release asset tag matches host", KnownTools.RuntimeTag == expected, expected);
    }

    /// <summary>
    /// Reads whatever Steam install this machine actually has. Informational when
    /// absent, a real assertion when present: a manifest that parses to appid 0 or
    /// name "Unknown" means the KeyValues reader regressed.
    /// </summary>
    private static void LiveSteamProbe()
    {
        SteamInstall? install;
        try { install = SteamLocator.Locate(); }
        catch (Exception ex)
        {
            Console.WriteLine($"  [info] steam probe failed: {ex.GetType().Name}");
            return;
        }

        if (install is null)
        {
            Console.WriteLine("  [info] no Steam install on this host — live probe skipped");
            return;
        }

        var apps = SteamLocator.EnumerateInstalled(install);
        Console.WriteLine($"  [info] steam root: {install.Root}");
        Console.WriteLine($"  [info] libraries: {install.Libraries.Count}, manifests: {apps.Count}");

        foreach (var app in apps.Take(5))
            Console.WriteLine($"  [info]   {app.AppId}  {app.Name}  [{app.InstallDir}]");

        if (apps.Count == 0)
        {
            Console.WriteLine("  [info] no appmanifest files present — parse assertion skipped");
            return;
        }

        var malformed = apps.Count(a => a.AppId == 0 || a.Name == "Unknown");
        Check("live: real appmanifest files parse", malformed == 0, $"{malformed}/{apps.Count} malformed");
    }
}
