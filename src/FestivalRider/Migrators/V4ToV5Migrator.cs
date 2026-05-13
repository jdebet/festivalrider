using System.Text.Json.Nodes;

namespace FestivalRider.Migrators;

// Plan 019. Moves bands and running orders from top-level AppState arrays into
// per-show nested arrays. ShowData becomes the true aggregate root.
//
// FROZEN ON SHIP. Bug fixes land as a successor migrator.
public sealed class V4ToV5Migrator : IStateMigrator
{
    public int FromVersion => 4;
    public int ToVersion => 5;

    private static Guid ParseGuid(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g) ? g : Guid.Empty;

    public JsonNode Migrate(JsonNode raw, IList<string> warnings)
    {
        if (raw is not JsonObject root)
            throw new InvalidOperationException("v4 payload root must be a JSON object.");

        var activeShowId = ParseGuid(root["activeShowId"]);

        var showsArray = root["shows"] is JsonArray sa ? sa : new JsonArray();
        var bandsArray = root["bands"] is JsonArray ba ? ba : new JsonArray();
        var runningOrdersArray = root["runningOrders"] is JsonArray roa ? roa : new JsonArray();

        // Build show lookup by id
        var showsById = new Dictionary<Guid, JsonObject>();
        var showsInOrder = new List<JsonObject>();
        foreach (var showNode in showsArray)
        {
            if (showNode is not JsonObject showObj) continue;
            var id = ParseGuid(showObj["id"]);
            if (id != Guid.Empty) showsById[id] = showObj;
            showsInOrder.Add(showObj);
        }

        // Build bandId -> first showId by scanning running order slots in document order
        var bandToShow = new Dictionary<Guid, Guid>();
        foreach (var roNode in runningOrdersArray)
        {
            if (roNode is not JsonObject roObj) continue;
            var showId = ParseGuid(roObj["showId"]);
            if (showId == Guid.Empty || !showsById.ContainsKey(showId)) continue;

            if (roObj["slots"] is JsonArray slots)
            {
                foreach (var slotNode in slots)
                {
                    if (slotNode is not JsonObject slotObj) continue;
                    var bandId = ParseGuid(slotObj["bandId"]);
                    if (bandId != Guid.Empty && !bandToShow.ContainsKey(bandId))
                        bandToShow[bandId] = showId;
                }
            }
        }

        // Distribute bands into shows
        foreach (var bandNode in bandsArray)
        {
            if (bandNode is not JsonObject bandObj) continue;
            var bandId = ParseGuid(bandObj["id"]);
            if (bandId == Guid.Empty) continue;

            var targetShowId = bandToShow.TryGetValue(bandId, out var sid) ? sid : activeShowId;
            if (targetShowId == Guid.Empty || !showsById.TryGetValue(targetShowId, out var targetShow))
            {
                // Fall back to first show if activeShowId doesn't match any show
                if (showsInOrder.Count > 0)
                {
                    targetShow = showsInOrder[0];
                    targetShowId = ParseGuid(targetShow["id"]);
                }
                else
                {
                    continue; // No shows at all — drop the band
                }
            }

            if (targetShow["bands"] is not JsonArray showBands)
            {
                showBands = new JsonArray();
                targetShow["bands"] = showBands;
            }
            showBands.Add(bandObj.DeepClone());
        }

        // Distribute running orders into shows
        foreach (var roNode in runningOrdersArray)
        {
            if (roNode is not JsonObject roObj) continue;
            var roShowId = ParseGuid(roObj["showId"]);

            JsonObject? targetShow = null;
            if (roShowId != Guid.Empty && showsById.TryGetValue(roShowId, out var ts))
            {
                targetShow = ts;
            }
            else if (activeShowId != Guid.Empty && showsById.TryGetValue(activeShowId, out var ashow))
            {
                targetShow = ashow;
                warnings.Add($"Running order with missing/unknown ShowId {roShowId} moved to active show.");
            }
            else if (showsInOrder.Count > 0)
            {
                targetShow = showsInOrder[0];
                warnings.Add($"Running order with missing/unknown ShowId {roShowId} moved to first show.");
            }

            if (targetShow is null) continue;

            if (targetShow["runningOrders"] is not JsonArray showRos)
            {
                showRos = new JsonArray();
                targetShow["runningOrders"] = showRos;
            }
            showRos.Add(roObj.DeepClone());
        }

        // Ensure every show has bands and runningOrders arrays
        foreach (var showObj in showsInOrder)
        {
            if (showObj["bands"] is not JsonArray)
                showObj["bands"] = new JsonArray();
            if (showObj["runningOrders"] is not JsonArray)
                showObj["runningOrders"] = new JsonArray();
        }

        // Remove top-level bands and runningOrders
        root.Remove("bands");
        root.Remove("runningOrders");

        // Update schema version
        root["schemaVersion"] = 5;

        return root;
    }
}
