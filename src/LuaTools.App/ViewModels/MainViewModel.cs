using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LuaTools.Core;

namespace LuaTools.App.ViewModels;

public sealed partial class InstalledAppViewModel : ViewModelBase
{
    public long AppId { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _installDir;
    [ObservableProperty] private string _sizeText;
    [ObservableProperty] private string _buildId;

    public InstalledAppViewModel(AppManifestFile manifest)
    {
        AppId = manifest.AppId;
        _name = manifest.Name;
        _installDir = manifest.InstallDir;
        _sizeText = FormatSize(manifest.SizeOnDisk);
        _buildId = manifest.BuildId.ToString();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 40 => $"{bytes / (double)(1L << 40):F1} TB",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F1} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        _ => $"{bytes / 1024.0:F0} KB"
    };
}

public sealed partial class SearchResultViewModel : ViewModelBase
{
    public long AppId { get; }
    public string Name { get; }

    public SearchResultViewModel(long appId, string name)
    {
        AppId = appId;
        Name = name;
    }

    public string Label => $"{Name}  ·  {AppId}";
}

public sealed partial class DepotRowViewModel : ViewModelBase
{
    public long DepotId { get; }
    public string Name { get; }
    public long ManifestId { get; }
    public string SizeText { get; }
    public bool IsDlc { get; }
    public bool SupportsMac { get; }
    public string PlatformLabel { get; }
    [ObservableProperty] private bool _isSelected = true;

    public DepotRowViewModel(DepotEntry depot)
    {
        DepotId = depot.DepotId;
        Name = string.IsNullOrWhiteSpace(depot.Name) ? $"Depot {depot.DepotId}" : depot.Name;
        ManifestId = depot.ManifestId;
        IsDlc = depot.IsDlc;
        SupportsMac = depot.SupportsMac;
        PlatformLabel = depot.PlatformLabel;
        SizeText = depot.Size switch
        {
            >= 1L << 30 => $"{depot.Size / (double)(1L << 30):F2} GB",
            >= 1L << 20 => $"{depot.Size / (double)(1L << 20):F1} MB",
            0 => "unknown",
            _ => $"{depot.Size / 1024.0:F0} KB"
        };
    }
}

public sealed partial class DownloadItemViewModel : ViewModelBase
{
    public long AppId { get; }
    public long[] DepotIds { get; }
    public string InstallDir { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _statusText = "Queued";
    [ObservableProperty] private double _percent;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private bool _isRunning;

    public DownloadItemViewModel(long appId, string name, long[] depotIds, string installDir)
    {
        AppId = appId;
        _name = name;
        DepotIds = depotIds;
        InstallDir = installDir;
    }
}

public sealed partial class BuildRowViewModel : ViewModelBase
{
    public long AppId { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _summary;

    public BuildRowViewModel(BuildStore.BuildEntry entry)
    {
        AppId = entry.AppId;
        _name = entry.Name;
        _summary = $"{entry.DepotManifests.Count} depot(s)";
    }
}

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly SteamApi _api = new();
    private readonly DepotDownloaderRunner _downloader = new();
    private readonly ConfigStore _config = ConfigStore.Load();
    private readonly BuildStore _builds = new();
    private CancellationTokenSource? _downloadCts;
    private SteamInstall? _steam;

    public ObservableCollection<InstalledAppViewModel> Installed { get; } = [];
    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = [];
    public ObservableCollection<DepotRowViewModel> Depots { get; } = [];
    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = [];
    public ObservableCollection<BuildRowViewModel> Builds { get; } = [];

    [ObservableProperty] private string _statusLine = "starting up";
    [ObservableProperty] private string _steamStatus = "Steam: looking…";
    [ObservableProperty] private string _toolStatus = "DepotDownloader: checking…";
    [ObservableProperty] private string _logTail = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedAppTitle = "no app selected";
    [ObservableProperty] private string _selectedAppMeta = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _canDownload;
    private long _selectedAppId;

    [ObservableProperty] private string _configSteamPath = "";
    [ObservableProperty] private string _configInstallRoot = "";
    [ObservableProperty] private string _configLanguage = "english";
    [ObservableProperty] private int _configMaxDownloads = 8;
    [ObservableProperty] private bool _configValidate;
    [ObservableProperty] private string _configTargetPlatform = "macos";
    [ObservableProperty] private bool _macOnly = true;

    public MainViewModel()
    {
        _configSteamPath = _config.Value.SteamPathOverride ?? "";
        _configInstallRoot = _config.Value.DefaultInstallRoot;
        _configLanguage = _config.Value.Language;
        _configMaxDownloads = _config.Value.MaxConcurrentDownloads;
        _configValidate = _config.Value.ValidateAfterDownload;
        _configTargetPlatform = _config.Value.TargetPlatform;

        Log.Message += line => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var text = line.Length > 160 ? line[..160] : line;
            LogTail = text;
        });

        _ = InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        _steam = SteamLocator.Locate(_config.Value.SteamPathOverride);
        SteamStatus = _steam is null
            ? "Steam: not found"
            : $"Steam: {_steam.Root}  ·  {_steam.Libraries.Count} librar{(_steam.Libraries.Count == 1 ? "y" : "ies")}";

        RefreshInstalled();
        RefreshBuilds();

        IsBusy = true;
        try
        {
            ToolStatus = _downloader.IsInstalled
                ? "DepotDownloader: ready"
                : "DepotDownloader: not installed (will fetch on first download)";
            StatusLine = _steam is null
                ? "Point Settings at your Steam folder to begin."
                : $"Ready · {Installed.Count} installed apps indexed";
        }
        finally
        {
            IsBusy = false;
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private void RefreshInstalled()
    {
        Installed.Clear();
        if (_steam is null) return;

        foreach (var manifest in SteamLocator.EnumerateInstalled(_steam))
            Installed.Add(new InstalledAppViewModel(manifest));

        StatusLine = $"{Installed.Count} installed apps";
    }

    [RelayCommand]
    private void RefreshBuilds()
    {
        Builds.Clear();
        foreach (var entry in _builds.List())
            Builds.Add(new BuildRowViewModel(entry));
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchText.Trim();
        if (query.Length < 2) return;

        IsBusy = true;
        try
        {
            var all = await _api.GetAppListAsync();
            SearchResults.Clear();

            foreach (var (id, name) in all
                         .Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(a => a.Name.Length)
                         .Take(80))
                SearchResults.Add(new SearchResultViewModel(id, name));

            StatusLine = $"{SearchResults.Count} match(es) for \"{query}\"";
        }
        catch (Exception ex)
        {
            StatusLine = $"search failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadAppAsync(SearchResultViewModel? result)
    {
        if (result is null) return;

        IsBusy = true;
        Depots.Clear();
        CanDownload = false;
        _selectedAppId = 0;
        SelectedAppTitle = result.Name;
        SelectedAppMeta = $"appid {result.AppId} · loading depots…";

        try
        {
            var info = await _api.GetAppInfoAsync(result.AppId);
            if (info is null)
            {
                SelectedAppMeta = $"appid {result.AppId} · no depot data";
                return;
            }

            _selectedAppId = info.AppId;

            var macDepots = info.MacDepots;
            var toShow = MacOnly ? macDepots : info.Depots;

            foreach (var depot in toShow)
                Depots.Add(new DepotRowViewModel(depot));

            var totalGb = toShow.Sum(d => d.Size) / (double)(1L << 30);

            if (!info.SupportsMac)
            {
                SelectedAppMeta = $"appid {info.AppId} · ❌ no macOS build — {info.Depots.Count} windows/linux depots only";
                StatusLine = $"{info.Name}: no macOS depots";
                return;
            }

            var macGb = macDepots.Sum(d => d.Size) / (double)(1L << 30);
            SelectedAppMeta = $"appid {info.AppId} · ✓ macOS · {macDepots.Count} mac depot(s) · " +
                              $"{macGb:F2} GB · {info.Developer ?? "?"}";
            StatusLine = $"{info.Name} loaded — macOS supported ({toShow.Count} depots shown, {totalGb:F2} GB)";
            CanDownload = true;
        }
        catch (Exception ex)
        {
            SelectedAppMeta = $"load failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void QueueDownload()
    {
        if (!CanDownload || _selectedAppId <= 0)
        {
            StatusLine = "select an app with a macOS build first";
            return;
        }

        var selected = Depots.Where(d => d.IsSelected).Select(d => d.DepotId).ToArray();
        if (selected.Length == 0)
        {
            StatusLine = "no depots selected";
            return;
        }

        var appId = _selectedAppId;
        var root = string.IsNullOrWhiteSpace(ConfigInstallRoot)
            ? Path.Combine(PlatformPaths.Home, "Games", "LuaTools")
            : ConfigInstallRoot;

        var dir = Path.Combine(root, Sanitize(SelectedAppTitle));
        var item = new DownloadItemViewModel(appId, SelectedAppTitle, selected, dir);
        Downloads.Add(item);
        StatusLine = $"queued {SelectedAppTitle} ({selected.Length} depots)";

        _ = RunDownloadAsync(item);
    }

    private async Task RunDownloadAsync(DownloadItemViewModel item)
    {
        item.IsRunning = true;
        item.StatusText = "preparing";
        item.IsIndeterminate = true;

        var progress = new Progress<DownloadProgress>(p =>
        {
            item.StatusText = p.Status.ToString();
            item.Detail = p.CurrentDepot ?? p.Error ?? "";
            item.IsIndeterminate = p.Status is DownloadStatus.FetchingManifest or DownloadStatus.Queued;
            if (p.Percent > 0) item.Percent = p.Percent;
        });

        try
        {
            _downloadCts ??= new CancellationTokenSource();

            var request = new DepotDownloaderRunner.DownloadRequest(
                item.AppId,
                item.Name,
                item.DepotIds,
                item.InstallDir,
                MaxDownloads: ConfigMaxDownloads,
                Validate: ConfigValidate,
                TargetOs: string.Equals(ConfigTargetPlatform, "auto", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : ConfigTargetPlatform);

            var result = await _downloader.DownloadAsync(request, progress, _downloadCts.Token);
            item.Percent = result.Success ? 100 : item.Percent;
            item.StatusText = result.Success ? "done" : "failed";
            item.Detail = result.Success ? item.InstallDir : result.Message;
            item.IsIndeterminate = false;
            StatusLine = result.Success ? $"installed {item.Name}" : $"failed {item.Name}: {result.Message}";
        }
        catch (Exception ex)
        {
            item.StatusText = "failed";
            item.Detail = ex.Message;
            StatusLine = $"download error: {ex.Message}";
        }
        finally
        {
            item.IsRunning = false;
        }
    }

    [RelayCommand]
    private void CancelDownload(DownloadItemViewModel? item)
    {
        _downloadCts?.Cancel();
        if (item is not null)
        {
            item.StatusText = "cancelled";
            item.IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task InstallToolAsync()
    {
        IsBusy = true;
        try
        {
            var reporter = new Progress<string>(Log.Info);
            var ok = await _downloader.EnsureToolAsync(reporter);
            ToolStatus = ok ? "DepotDownloader: ready" : "DepotDownloader: install failed";
            StatusLine = ToolStatus;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _config.Value.SteamPathOverride = string.IsNullOrWhiteSpace(ConfigSteamPath) ? null : ConfigSteamPath;
        _config.Value.DefaultInstallRoot = ConfigInstallRoot;
        _config.Value.Language = ConfigLanguage;
        _config.Value.MaxConcurrentDownloads = ConfigMaxDownloads;
        _config.Value.ValidateAfterDownload = ConfigValidate;
        _config.Value.TargetPlatform = ConfigTargetPlatform;
        _config.Save();

        _steam = SteamLocator.Locate(_config.Value.SteamPathOverride);
        RefreshInstalled();
        StatusLine = "settings saved";
    }

    [RelayCommand]
    private static void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        try
        {
            var psi = PlatformPaths.IsMac
                ? new ProcessStartInfo("open", path)
                : PlatformPaths.IsWindows
                    ? new ProcessStartInfo("explorer.exe", path)
                    : new ProcessStartInfo("xdg-open", path);
            psi.UseShellExecute = PlatformPaths.IsWindows;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Warn($"open folder failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ExportBuild(BuildRowViewModel? row)
    {
        if (row is null) return;
        var entry = _builds.Get(row.AppId);
        if (entry is null) return;
        var path = _builds.WriteLua(entry);
        StatusLine = $"lua written: {path}";
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "app" : cleaned;
    }
}
