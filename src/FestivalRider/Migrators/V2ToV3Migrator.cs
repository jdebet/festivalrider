using System.Text.Json.Nodes;

namespace FestivalRider.Migrators;

// Plan 006 (multi-show) + plan 008 (framework). Wraps the v2 single-`showData` root
// into v3's `shows: [showData]` + `activeShowId`, and stamps every pre-existing
// `RunningOrder` with that show's id so v3's per-show scoping holds.
//
// FROZEN ON SHIP. Bug fixes land as a successor migrator.
public sealed class V2ToV3Migrator : IStateMigrator
{
    public int FromVersion => 2;
    public int ToVersion => 3;

    public JsonNode Migrate(JsonNode raw, IList<string> warnings)
    {
        if (raw is not JsonObject root)
            throw new InvalidOperationException("v2 payload root must be a JSON object.");

        // Pull the v2 single show out of the root (or default it if absent).
        JsonObject show;
        if (root["showData"] is JsonObject existing)
        {
            // Detach so we can re-parent under `shows`.
            root.Remove("showData");
            show = existing;
        }
        else
        {
            show = new JsonObject
            {
                ["name"] = "Untitled show",
                ["address"] = null,
                ["dateOfOpening"] = "0001-01-01",
                ["showDayCount"] = 1,
                ["stages"] = new JsonArray()
            };
            warnings.Add("v2 payload had no showData; seeded a default show.");
        }

        // Show needs an `id` in v3. Reuse if present and parseable, otherwise mint one.
        Guid showId;
        if (show["id"] is JsonValue idVal && idVal.TryGetValue<string>(out var idStr) && Guid.TryParse(idStr, out var parsed))
            showId = parsed;
        else
            showId = Guid.NewGuid();

        show["id"] = showId.ToString();

        root["shows"] = new JsonArray(show);
        root["activeShowId"] = showId.ToString();

        // Stamp running orders so they belong to the migrated show.
        if (root["runningOrders"] is JsonArray ros)
        {
            int stamped = 0;
            foreach (var ro in ros)
            {
                if (ro is not JsonObject roObj) continue;
                if (roObj["showId"] is null
                    || (roObj["showId"] is JsonValue rsv
                        && rsv.TryGetValue<string>(out var rs)
                        && (string.IsNullOrWhiteSpace(rs) || rs == Guid.Empty.ToString())))
                {
                    roObj["showId"] = showId.ToString();
                    stamped++;
                }
            }
            if (stamped > 0)
                warnings.Add($"Assigned {stamped} running order(s) to the migrated show.");
        }
        else
        {
            root["runningOrders"] = new JsonArray();
        }

        return root;
    }
}
