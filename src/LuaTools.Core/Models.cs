namespace LuaTools.Core;

public sealed record SteamLibrary(string Path, IReadOnlyList<long> AppIds)
{
    public string SteamAppsDir => System.IO.Path.Combine(Path, "steamapps");
    public string CommonDir => System.IO.Path.Combine(SteamAppsDir, "common");
    public string DepotCacheDir => System.IO.Path.Combine(SteamAppsDir, "depotcache");
}

public sealed record SteamInstall(string Root, string SteamAppsDir, IReadOnlyList<SteamLibrary> Libraries)
{
    public string DepotCacheDir => System.IO.Path.Combine(SteamAppsDir, "depotcache");
    public string LibraryFoldersFile => System.IO.Path.Combine(SteamAppsDir, "libraryfolders.vdf");
    public string UserDataDir => System.IO.Path.Combine(Root, "userdata");

    public string? FindInstallDir(string folderName) =>
        Libraries
            .Select(l => System.IO.Path.Combine(l.CommonDir, folderName))
            .FirstOrDefault(Directory.Exists);
}

public sealed record DepotEntry(
    long DepotId,
    long ManifestId,
    long Size,
    string Name,
    bool IsDlc,
    long? DlcAppId,
    IReadOnlyList<string> OsList)
{
    /// <summary>Depot with no os restriction, or one that explicitly ships a macos build.</summary>
    public bool SupportsMac =>
        OsList.Count == 0 || OsList.Contains("macos", StringComparer.OrdinalIgnoreCase);

    public string PlatformLabel => OsList.Count == 0 ? "shared" : string.Join("+", OsList);
}

public sealed record AppDefinition(
    long AppId,
    string Name,
    string? Type,
    string? Developer,
    string? Publisher,
    IReadOnlyList<DepotEntry> Depots,
    IReadOnlyList<long> DlcIds,
    ulong? LastUpdated)
{
    public long TotalSize => Depots.Sum(d => d.Size);
    /// <summary>True when at least one depot ships a macos artifact.</summary>
    public bool SupportsMac => Depots.Any(d => d.SupportsMac && d.OsList.Count > 0);
    public IReadOnlyList<DepotEntry> MacDepots => Depots.Where(d => d.SupportsMac).ToArray();
}

public sealed record AppManifestFile(
    long AppId,
    string Name,
    string InstallDir,
    string StateFlags,
    long SizeOnDisk,
    long BuildId,
    long LastUpdated,
    IReadOnlyList<long> DepotIds,
    string? ManifestPath);

public enum DownloadStatus
{
    Queued,
    FetchingManifest,
    Downloading,
    Installing,
    Done,
    Failed,
    Cancelled,
    Paused
}

public sealed record DownloadProgress(
    DownloadStatus Status,
    long BytesRead,
    long TotalBytes,
    double Percent,
    string? CurrentDepot,
    string? Error)
{
    public static DownloadProgress Queued() => new(DownloadStatus.Queued, 0, 0, 0, null, null);
}

public sealed record DependencyTool(string Name, string ExeName, string DownloadUrl, string AssetPattern, string[] RequiredFiles);

public static class KnownTools
{
    public static readonly DependencyTool DepotDownloader = new(
        "DepotDownloader",
        "DepotDownloader" + PlatformPaths.ExecutableExtension,
        "https://api.github.com/repos/SteamRE/DepotDownloader/releases/latest",
        "DepotDownloader-{runtime}.zip",
        ["DepotDownloader.dll", "DepotDownloader" + PlatformPaths.ExecutableExtension]);

    /// <summary>
    /// GitHub release asset token. Note: the SteamRE release publishes mac assets as
    /// "macos-arm64"/"macos-x64" — not the .NET runtime identifier "osx-*".
    /// </summary>
    public static string RuntimeTag
    {
        get
        {
            var arm = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
                      System.Runtime.InteropServices.Architecture.Arm64;

            if (PlatformPaths.IsMac) return arm ? "macos-arm64" : "macos-x64";
            if (PlatformPaths.IsWindows) return arm ? "windows-arm64" : "windows-x64";
            return arm ? "linux-arm64" : "linux-x64";
        }
    }

    /// <summary>.NET RID used for build/publish, distinct from the release asset name.</summary>
    public static string RuntimeIdentifier => PlatformPaths.IsMac
        ? (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
           System.Runtime.InteropServices.Architecture.Arm64 ? "osx-arm64" : "osx-x64")
        : PlatformPaths.IsWindows ? "win-x64" : "linux-x64";
}
