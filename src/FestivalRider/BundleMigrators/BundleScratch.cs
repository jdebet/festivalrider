using System.Text.Json;

namespace FestivalRider.BundleMigrators;

// Plan 013. Mutable view of a bundle payload during migration. Manifest is a
// loose property bag (camelCase keys, exactly matching what BundleService
// emits) so future migrators can introduce or rename fields without churn.
// Entries map zip full-name -> UTF-8 text. SchemaVersion mirrors
// Manifest["schemaVersion"] and is kept in sync by the pipeline.
public sealed class BundleScratch
{
    public IDictionary<string, object?> Manifest { get; }
    public IDictionary<string, string> Entries { get; }
    public int SchemaVersion { get; set; }

    public BundleScratch(
        IDictionary<string, object?> manifest,
        IDictionary<string, string> entries,
        int schemaVersion)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        SchemaVersion = schemaVersion;
    }

    // Pinned options for manifest (de)serialization. Keys are taken verbatim;
    // bundle JSON already uses camelCase, so no naming policy is needed here.
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true,
    };

    public static IDictionary<string, object?> ParseManifest(string json)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions)
            ?? throw new InvalidOperationException("Manifest JSON parsed to null.");
        return dict;
    }

    public string SerializeManifest() =>
        JsonSerializer.Serialize(Manifest, JsonOptions);

    // Helper for migrators reading scalar string fields out of the property bag.
    // Values may be either CLR strings (set by an earlier migrator) or JsonElement
    // (fresh off Deserialize). Anything else returns null.
    public static string? TryGetString(IDictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null) return null;
        return v switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => null,
        };
    }
}
