using System.Text.Json.Nodes;

namespace FestivalRider.Migrators;

// Plan 017. Adds Cable.MaxLengthMeters, LightingMachine.Location,
// FohSound.OutputProtocolOther/OutputLocationOther, replaces
// bool StageToFohRoundTrip with int StageToFohRoundTripCount.
//
// FROZEN ON SHIP. Bug fixes land as a successor migrator.
public sealed class V3ToV4Migrator : IStateMigrator
{
    public int FromVersion => 3;
    public int ToVersion => 4;

    public JsonNode Migrate(JsonNode raw, IList<string> warnings)
    {
        if (raw is not JsonObject root)
            throw new InvalidOperationException("v3 payload root must be a JSON object.");

        if (root["bands"] is JsonArray bands)
        {
            foreach (var band in bands)
            {
                if (band is not JsonObject b) continue;
                if (b["rider"] is not JsonObject rider) continue;
                if (rider["tech"] is not JsonObject tech) continue;

                // Cables: add maxLengthMeters null
                if (tech["cables"] is JsonArray cables)
                {
                    foreach (var c in cables)
                    {
                        if (c is not JsonObject cable) continue;
                        if (cable["maxLengthMeters"] is null)
                            cable["maxLengthMeters"] = null;
                    }
                }

                // Lighting floor machines: add location null
                if (tech["lighting"] is JsonObject lighting && lighting["floorMachines"] is JsonArray machines)
                {
                    foreach (var m in machines)
                    {
                        if (m is not JsonObject machine) continue;
                        if (machine["location"] is null)
                            machine["location"] = null;
                    }
                }

                // FOH: convert bool StageToFohRoundTrip -> int StageToFohRoundTripCount,
                // add OutputProtocolOther / OutputLocationOther nulls
                if (tech["foh"] is JsonObject foh)
                {
                    if (foh["stageToFohRoundTrip"] is JsonValue rtVal && rtVal.TryGetValue<bool>(out var rtBool))
                    {
                        foh.Remove("stageToFohRoundTrip");
                        foh["stageToFohRoundTripCount"] = rtBool ? 1 : 0;
                    }
                    else if (foh["stageToFohRoundTripCount"] is null)
                    {
                        foh["stageToFohRoundTripCount"] = 0;
                    }

                    if (foh["outputProtocolOther"] is null)
                        foh["outputProtocolOther"] = null;
                    if (foh["outputLocationOther"] is null)
                        foh["outputLocationOther"] = null;
                }
            }
        }

        return root;
    }
}
