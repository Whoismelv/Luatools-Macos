using System.Net.Http.Headers;
using System.Text.Json;

namespace LuaTools.Core;

public sealed class SteamApi : IDisposable
{
    private const string AppListUrl = "https://api.steampowered.com/ISteamApps/GetAppList/v2/";
    private const string SteamCmdInfoUrl = "https://api.steamcmd.net/v1/info/";
    private const string FeaturedUrl = "https://store.steampowered.com/api/featuredcategories";

    private readonly HttpClient _http;

    public SteamApi(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<IReadOnlyList<(long AppId, string Name)>> GetAppListAsync(CancellationToken ct = default)
    {
        var cache = Path.Combine(PlatformPaths.CacheDir, "steam-applist.json");
        try
        {
            var json = await _http.GetStringAsync(AppListUrl, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(cache, json, ct).ConfigureAwait(false);
            return ParseAppList(json);
        }
        catch (Exception ex) when (File.Exists(cache))
        {
            Log.Warn($"app list download failed, using cache: {ex.Message}");
            return ParseAppList(await File.ReadAllTextAsync(cache, ct).ConfigureAwait(false));
        }
    }

    private static IReadOnlyList<(long, string)> ParseAppList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("applist", out var appList) ||
            !appList.TryGetProperty("apps", out var apps))
            return [];

        var list = new List<(long, string)>(apps.GetArrayLength());
        foreach (var app in apps.EnumerateArray())
        {
            var id = app.GetProperty("appid").GetInt64();
            var name = app.GetProperty("name").GetString();
            if (id > 0 && !string.IsNullOrWhiteSpace(name)) list.Add((id, name));
        }
        return list;
    }

    public async Task<AppDefinition?> GetAppInfoAsync(long appId, CancellationToken ct = default)
    {
        var text = await _http.GetStringAsync($"{SteamCmdInfoUrl}{appId}", ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);

        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty(appId.ToString(), out var app))
            return null;

        var common = app.TryGetProperty("common", out var c) ? c : default;
        var depots = new List<DepotEntry>();
        var dlcIds = new List<long>();

        if (app.TryGetProperty("depots", out var depotNode))
        {
            if (depotNode.TryGetProperty("dlcappid", out var dlcIdsNode))
            {
                foreach (var node in dlcIdsNode.EnumerateObject())
                    if (long.TryParse(node.Name, out var dlcId)) dlcIds.Add(dlcId);
            }

            if (depotNode.TryGetProperty("listofdlc", out var listNode) &&
                listNode.ValueKind == JsonValueKind.String)
            {
                foreach (var part in (listNode.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (long.TryParse(part.Trim(), out var dlcId) && !dlcIds.Contains(dlcId)) dlcIds.Add(dlcId);
            }

            foreach (var depotProp in depotNode.EnumerateObject())
            {
                if (!long.TryParse(depotProp.Name, out var depotId)) continue;
                if (depotProp.Value.ValueKind != JsonValueKind.Object) continue;

                var value = depotProp.Value;
                var manifest = value.TryGetProperty("manifests", out var m) &&
                               m.TryGetProperty("public", out var pub) &&
                               pub.TryGetProperty("gid", out var gid)
                    ? long.TryParse(gid.GetString(), out var parsed) ? parsed : 0
                    : 0;

                var size = value.TryGetProperty("maxsize", out var ms) && long.TryParse(ms.GetString(), out var sz)
                    ? sz
                    : 0;

                var name = value.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var isDlc = value.TryGetProperty("dlcappid", out _);

                long? configuredDlc = null;
                if (value.TryGetProperty("dlcappid", out var d) && long.TryParse(d.GetString(), out var dv))
                    configuredDlc = dv;

                var osList = Array.Empty<string>();
                if (value.TryGetProperty("config", out var cfg) &&
                    cfg.ValueKind == JsonValueKind.Object &&
                    cfg.TryGetProperty("oslist", out var osNode) &&
                    osNode.ValueKind == JsonValueKind.String)
                {
                    osList = (osNode.GetString() ?? "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }

                depots.Add(new DepotEntry(depotId, manifest, size, name, isDlc, configuredDlc, osList));
            }
        }

        var type = common.ValueKind == JsonValueKind.Object && common.TryGetProperty("type", out var t)
            ? t.GetString()
            : null;

        var name2 = common.ValueKind == JsonValueKind.Object && common.TryGetProperty("name", out var nm)
            ? nm.GetString() ?? appId.ToString()
            : appId.ToString();

        var developer = common.ValueKind == JsonValueKind.Object && common.TryGetProperty("developer", out var dev)
            ? dev.GetString()
            : null;

        var publisher = common.ValueKind == JsonValueKind.Object && common.TryGetProperty("publisher", out var pubr)
            ? pubr.GetString()
            : null;

        ulong? lastUpdated = null;
        if (app.TryGetProperty("depots", out var dep) &&
            dep.TryGetProperty("baselanguages", out _))
        {
            // present but not a timestamp; left null deliberately
        }

        return new AppDefinition(appId, name2, type, developer, publisher,
            depots.OrderBy(d => d.DepotId).ToArray(), dlcIds, lastUpdated);
    }

    public async Task<Dictionary<long, (string Header, string? Capsule)>> GetStoreArtworkAsync(
        IEnumerable<long> appIds, CancellationToken ct = default)
    {
        var result = new Dictionary<long, (string, string?)>();
        foreach (var id in appIds.Distinct())
        {
            try
            {
                var json = await _http.GetStringAsync(
                    $"https://store.steampowered.com/api/appdetails?appids={id}&filters=basic", ct)
                    .ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty(id.ToString(), out var root)) continue;
                if (!root.TryGetProperty("success", out var ok) || !ok.GetBoolean()) continue;
                if (!root.TryGetProperty("data", out var data)) continue;

                var header = data.TryGetProperty("header_image", out var h) ? h.GetString() ?? "" : "";
                var capsule = data.TryGetProperty("capsule_image", out var cap) ? cap.GetString() : null;
                if (header.Length > 0) result[id] = (header, capsule);
            }
            catch (Exception ex)
            {
                Log.Warn($"appdetails {id} failed: {ex.Message}");
            }
        }
        return result;
    }

    public void Dispose() => _http.Dispose();
}
