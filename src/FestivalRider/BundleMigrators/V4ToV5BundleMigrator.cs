using System.Text.Json;

namespace FestivalRider.BundleMigrators;

// Plan 019. Converts a v4 multi-show full-AppState bundle into a v5 single-show
// bundle containing only the active show. Non-active shows and their running
// orders are dropped. All band entries are kept verbatim.
//
// FROZEN ON SHIP. Bug fixes land as a successor migrator.
public sealed class V4ToV5BundleMigrator : IBundleMigrator
{
    public int FromVersion => 4;
    public int ToVersion => 5;

    public void Migrate(BundleScratch scratch, IList<string> warnings)
    {
        if (scratch is null) throw new ArgumentNullException(nameof(scratch));
        if (warnings is null) throw new ArgumentNullException(nameof(warnings));

        // Read v4 manifest fields
        var activeShowId = TryGetGuid(scratch.Manifest, "activeShowId") ?? Guid.Empty;

        var shows = TryGetStringList(scratch.Manifest, "shows");
        var bands = TryGetStringList(scratch.Manifest, "bands");
        var runningOrders = TryGetStringList(scratch.Manifest, "runningOrders");

        // Find the active show entry
        string? activeShowPath = null;
        foreach (var path in shows)
        {
            var id = ParseIdFromPath(path, "shows/");
            if (id == activeShowId)
            {
                activeShowPath = path;
                break;
            }
        }

        if (activeShowPath is null && shows.Count > 0)
        {
            activeShowPath = shows[0];
            warnings.Add($"Active show {activeShowId} not found in manifest; falling back to first show.");
        }

        // Warn about dropped shows
        if (shows.Count > 1)
        {
            warnings.Add($"v4 bundle contained {shows.Count} shows; only the active show was kept.");
        }

        // Filter running orders: keep only those whose CSV ShowId == activeShowId
        var keptRunningOrders = new List<string>();
        foreach (var roPath in runningOrders)
        {
            if (!scratch.Entries.TryGetValue(roPath, out var csv))
            {
                warnings.Add($"Running order entry {roPath} missing from bundle; skipped.");
                continue;
            }
            var csvShowId = PeekShowIdFromCsv(csv);
            if (csvShowId == activeShowId)
            {
                keptRunningOrders.Add(roPath);
            }
            else
            {
                warnings.Add($"Running order {roPath} belongs to show {csvShowId} and was dropped during v4→v5 migration.");
            }
        }

        // Remove non-active show entries from scratch
        foreach (var path in shows)
        {
            if (path != activeShowPath)
                scratch.Entries.Remove(path);
        }

        // Remove dropped running order entries from scratch
        foreach (var path in runningOrders)
        {
            if (!keptRunningOrders.Contains(path))
                scratch.Entries.Remove(path);
        }

        // Rewrite manifest to v5 single-show shape
        scratch.Manifest.Remove("shows");
        scratch.Manifest.Remove("activeShowId");
        scratch.Manifest["show"] = activeShowPath ?? string.Empty;
        scratch.Manifest["bands"] = bands;
        scratch.Manifest["runningOrders"] = keptRunningOrders;
    }

    private static List<string> TryGetStringList(IDictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null) return new List<string>();
        if (v is List<string> list) return list;
        if (v is IEnumerable<object?> objList)
        {
            var result = new List<string>();
            foreach (var item in objList)
            {
                if (item is string s)
                    result.Add(s);
                else if (item is JsonElement jel && jel.ValueKind == JsonValueKind.String)
                    result.Add(jel.GetString()!);
            }
            return result;
        }
        if (v is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var result = new List<string>();
            foreach (var item in je.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    result.Add(item.GetString()!);
            }
            return result;
        }
        return new List<string>();
    }

    private static Guid? TryGetGuid(IDictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null) return null;
        if (v is Guid g) return g;
        if (v is string s && Guid.TryParse(s, out var parsed)) return parsed;
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.String && Guid.TryParse(je.GetString(), out parsed))
                return parsed;
        }
        return null;
    }

    private static Guid? ParseIdFromPath(string path, string prefix)
    {
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var name = Path.GetFileNameWithoutExtension(path[prefix.Length..]);
        return Guid.TryParse(name, out var g) ? g : null;
    }

    private static Guid PeekShowIdFromCsv(string csv)
    {
        using var sr = new StringReader(csv);
        _ = sr.ReadLine(); // header
        var first = sr.ReadLine();
        if (string.IsNullOrEmpty(first)) return Guid.Empty;
        var firstField = first.Split(',', 2)[0].Trim('"');
        return Guid.TryParse(firstField, out var g) ? g : Guid.Empty;
    }
}
