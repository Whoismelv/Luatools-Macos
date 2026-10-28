using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace LuaTools.Core;

public sealed partial class DepotDownloaderRunner
{
    [GeneratedRegex(@"^\s*(?<pct>[0-9]+(?:\.[0-9]+)?)%\s+(?<msg>.+)$")]
    private static partial Regex PercentLine();

    private readonly HttpClient _http;

    public DepotDownloaderRunner(HttpClient? http = null) =>
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

    public string ToolDirectory => Path.Combine(PlatformPaths.ToolsDir, "depotdownloader");

    public string ToolPath => Path.Combine(ToolDirectory,
        PlatformPaths.IsWindows ? "DepotDownloader.exe" : "DepotDownloader");

    public bool IsInstalled => File.Exists(ToolPath);

    /// <summary>
    /// Ensures DepotDownloader is present. The SteamRE release ships a
    /// self-contained single-file binary for every runtime, so that is the only
    /// path used. A global `dotnet tool` install is framework-dependent and
    /// cannot be relocated by copying two files; the app bundles its own .NET
    /// but an external process would still need a system runtime.
    /// </summary>
    public async Task<bool> EnsureToolAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        if (IsInstalled) return true;

        Directory.CreateDirectory(ToolDirectory);
        return await DownloadReleaseAsync(log, ct).ConfigureAwait(false);
    }

    private async Task<bool> DownloadReleaseAsync(IProgress<string>? log, CancellationToken ct)
    {
        try
        {
            log?.Report("fetching DepotDownloader release");
            using var req = new HttpRequestMessage(HttpMethod.Get, KnownTools.DepotDownloader.DownloadUrl);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            req.Headers.TryAddWithoutValidation("User-Agent", "LuaToolsMac/1.0");

            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                log?.Report($"release lookup failed: {(int)res.StatusCode}");
                return false;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(
                await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));

            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (!doc.RootElement.TryGetProperty("assets", out var assets)) return false;

            var runtime = KnownTools.RuntimeTag;
            string? url = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.Contains(runtime, StringComparison.OrdinalIgnoreCase)) continue;
                url = asset.TryGetProperty("browser_download_url", out var b) ? b.GetString() : null;
                if (url is not null) break;
            }

            if (url is null)
            {
                log?.Report($"no {runtime} asset in release {tag}");
                foreach (var asset in assets.EnumerateArray())
                    log?.Report("  asset: " + (asset.TryGetProperty("name", out var n2) ? n2.GetString() : "?"));
                return false;
            }

            log?.Report($"downloading {url}");
            var zipPath = Path.Combine(PlatformPaths.CacheDir, "depotdownloader.zip");
            await using (var fs = File.Create(zipPath))
            await using (var stream = await _http.GetStreamAsync(url, ct).ConfigureAwait(false))
                await stream.CopyToAsync(fs, ct).ConfigureAwait(false);

            ZipFile.ExtractToDirectory(zipPath, ToolDirectory, overwriteFiles: true);

            foreach (var exe in Directory.EnumerateFiles(ToolDirectory, "DepotDownloader*",
                         SearchOption.AllDirectories))
            {
                if (exe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                if (!PlatformPaths.IsWindows) MakeExecutable(exe);
            }

            var found = Directory.EnumerateFiles(ToolDirectory, "DepotDownloader*", SearchOption.AllDirectories)
                .FirstOrDefault(f => !f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            if (found is null) return false;

            if (!PathsEqual(found, ToolPath))
                File.Copy(found, ToolPath, overwrite: true);
            if (!PlatformPaths.IsWindows) MakeExecutable(ToolPath);

            log?.Report("DepotDownloader ready");
            return true;
        }
        catch (Exception ex)
        {
            log?.Report($"DepotDownloader acquisition failed: {ex.Message}");
            return false;
        }
    }

    public sealed record DownloadRequest(
        long AppId,
        string AppName,
        IReadOnlyList<long> DepotIds,
        string InstallDirectory,
        long? ManifestId = null,
        string? ManifestFilePath = null,
        int MaxDownloads = 8,
        bool Validate = false,
        string? DepotKeysFile = null,
        string? Username = null,
        string? Password = null,
        string? Branch = "public",
        string? TargetOs = null,
        string? TargetArch = null);

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!IsInstalled && !await EnsureToolAsync(null, ct).ConfigureAwait(false))
            return new DownloadResult(false, "DepotDownloader unavailable", "");

        if (!Directory.Exists(request.InstallDirectory))
            Directory.CreateDirectory(request.InstallDirectory);

        var psi = new ProcessStartInfo(ToolPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = ToolDirectory
        };

        psi.ArgumentList.Add("-app");
        psi.ArgumentList.Add(request.AppId.ToString());
        psi.ArgumentList.Add("-dir");
        psi.ArgumentList.Add(request.InstallDirectory);
        psi.ArgumentList.Add("-max-downloads");
        psi.ArgumentList.Add(request.MaxDownloads.ToString());

        foreach (var depot in request.DepotIds)
        {
            psi.ArgumentList.Add("-depot");
            psi.ArgumentList.Add(depot.ToString());
        }

        if (request.ManifestId is > 0)
        {
            psi.ArgumentList.Add("-manifest");
            psi.ArgumentList.Add(request.ManifestId.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(request.ManifestFilePath))
        {
            psi.ArgumentList.Add("-manifestfile");
            psi.ArgumentList.Add(request.ManifestFilePath!);
        }

        if (!string.IsNullOrWhiteSpace(request.DepotKeysFile))
        {
            psi.ArgumentList.Add("-depotkeys");
            psi.ArgumentList.Add(request.DepotKeysFile!);
        }

        if (request.Validate) psi.ArgumentList.Add("-validate");

        if (!string.IsNullOrWhiteSpace(request.TargetOs))
        {
            psi.ArgumentList.Add("-os");
            psi.ArgumentList.Add(request.TargetOs!);
        }

        if (!string.IsNullOrWhiteSpace(request.TargetArch))
        {
            psi.ArgumentList.Add("-osarch");
            psi.ArgumentList.Add(request.TargetArch!);
        }

        if (!string.IsNullOrWhiteSpace(request.Branch) && request.Branch != "public")
        {
            psi.ArgumentList.Add("-branch");
            psi.ArgumentList.Add(request.Branch!);
        }

        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            psi.ArgumentList.Add("-username");
            psi.ArgumentList.Add(request.Username!);
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                psi.ArgumentList.Add("-password");
                psi.ArgumentList.Add(request.Password!);
                psi.ArgumentList.Add("-remember-password");
            }
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stderr = new StringBuilder();

        var started = new TaskCompletionSource();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            started.TrySetResult();
            HandleLine(e.Data, progress);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            HandleLine(e.Data, progress);
        };

        progress?.Report(new DownloadProgress(DownloadStatus.Downloading, 0, 0, 0, null, null));
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var reg = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
        });

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

        if (ct.IsCancellationRequested)
            return new DownloadResult(false, "cancelled", stderr.ToString());

        var ok = process.ExitCode == 0;
        progress?.Report(new DownloadProgress(
            ok ? DownloadStatus.Done : DownloadStatus.Failed,
            0, 0, ok ? 100 : 0, null,
            ok ? null : stderr.ToString().Trim()));

        return new DownloadResult(ok, ok ? "ok" : $"exit {process.ExitCode}", stderr.ToString());
    }

    private static void HandleLine(string line, IProgress<DownloadProgress>? progress)
    {
        if (progress is null) return;

        var match = PercentLine().Match(line);
        if (match.Success && double.TryParse(match.Groups["pct"].Value,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
        {
            progress.Report(new DownloadProgress(
                DownloadStatus.Downloading, 0, 0, pct, match.Groups["msg"].Value.Trim(), null));
            return;
        }

        if (line.Contains("Validating", StringComparison.OrdinalIgnoreCase))
            progress.Report(new DownloadProgress(DownloadStatus.Installing, 0, 0, 100, line.Trim(), null));
        else if (line.Contains("Downloading depot", StringComparison.OrdinalIgnoreCase))
            progress.Report(new DownloadProgress(DownloadStatus.FetchingManifest, 0, 0, 0, line.Trim(), null));
    }

    public static string? FindOnPath(string name)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var extra = new List<string>();
        if (PlatformPaths.IsMac)
            extra.AddRange(["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin",
                Path.Combine(PlatformPaths.Home, ".dotnet", "tools")]);
        else if (PlatformPaths.IsWindows)
            extra.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools"));
        else
            extra.AddRange(["/usr/local/bin", "/usr/bin", "/bin", Path.Combine(PlatformPaths.Home, ".dotnet", "tools")]);

        var exts = PlatformPaths.IsWindows
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';')
            : [""];

        foreach (var dir in paths.Concat(extra).Distinct())
        {
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate)) return candidate;
            }
            var direct = Path.Combine(dir, name);
            if (File.Exists(direct)) return direct;
        }
        return null;
    }

    public static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (Exception ex)
        {
            Log.Warn($"chmod failed for {path}: {ex.Message}");
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            PlatformPaths.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

public sealed record DownloadResult(bool Success, string Message, string Stderr);
