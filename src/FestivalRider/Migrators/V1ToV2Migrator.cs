using System.Text.Json.Nodes;

namespace FestivalRider.Migrators;

// Plan 008. Maps the 001 single-`Band`-list shape onto 002's structured `AppState`:
// - drops `Band.Genre` (warn once with count)
// - drops every `Band.Rider.Tech.Inputs` (warn with total)
// - drops every `Band.Rider.Tech.Backline*` collection (warn with total)
// - adds a freshly-defaulted `showData` root if absent
// - preserves `runningOrders` intact (stage references default to "Unknown stage" downstream)
//
// FROZEN ON SHIP (plan 008 Architecture rules). Bug fixes land as a successor migrator.
public sealed class V1ToV2Migrator : IStateMigrator
{
    public int FromVersion => 1;
    public int ToVersion => 2;

    public JsonNode Migrate(JsonNode raw, IList<string> warnings)
    {
        if (raw is not JsonObject root)
            throw new InvalidOperationException("v1 payload root must be a JSON object.");

        int genreDropped = 0;
        int inputsDropped = 0;
        int backlineDropped = 0;

        if (root["bands"] is JsonArray bands)
        {
            foreach (var node in bands)
            {
                if (node is not JsonObject band) continue;

                if (band.Remove("genre")) genreDropped++;

                if (band["rider"] is JsonObject rider && rider["tech"] is JsonObject tech)
                {
                    if (tech["inputs"] is JsonArray inputs)
                    {
                        inputsDropped += inputs.Count;
                        tech.Remove("inputs");
                    }

                    // Drop any property whose key starts with "backline" (case-insensitive),
                    // counting array entries when present, otherwise counting one per dropped key.
                    var backlineKeys = tech
                        .Where(kvp => kvp.Key.StartsWith("backline", StringComparison.OrdinalIgnoreCase))
                        .Select(kvp => kvp.Key)
                        .ToList();
                    foreach (var key in backlineKeys)
                    {
                        backlineDropped += tech[key] is JsonArray arr ? arr.Count : 1;
                        tech.Remove(key);
                    }
                }
            }
        }

        if (root["showData"] is null)
        {
            root["showData"] = new JsonObject
            {
                ["name"] = "Untitled show",
                ["address"] = null,
                ["dateOfOpening"] = "0001-01-01",
                ["showDayCount"] = 1,
                ["stages"] = new JsonArray()
            };
        }

        if (root["runningOrders"] is null)
            root["runningOrders"] = new JsonArray();

        if (genreDropped > 0)
            warnings.Add($"Dropped Band.Genre on {genreDropped} band(s); v2 has no equivalent.");
        if (inputsDropped > 0)
            warnings.Add($"Dropped {inputsDropped} TechRider.Inputs row(s); v2 has no equivalent.");
        if (backlineDropped > 0)
            warnings.Add($"Dropped {backlineDropped} TechRider.Backline* row(s); v2 has no equivalent.");

        return root;
    }
}
