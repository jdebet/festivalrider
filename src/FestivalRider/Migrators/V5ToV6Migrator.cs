using System.Globalization;
using System.Text.Json.Nodes;

namespace FestivalRider.Migrators;

// Plan 020. Upgrades every RunningOrderSlot from v5 flat record to v6 event-chain model.
//
// FROZEN ON SHIP. Bug fixes land as a successor migrator.
public sealed class V5ToV6Migrator : IStateMigrator
{
    public int FromVersion => 5;
    public int ToVersion => 6;

    private static Guid ParseGuid(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g) ? g : Guid.Empty;

    private static DateOnly ParseDateOnly(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d) ? d : default;

    private static int ParseInt(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;

    private static int? ParseNullableInt(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonValue v && v.TryGetValue<int>(out var n)) return n;
        return null;
    }

    private static string? ParseString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public JsonNode Migrate(JsonNode raw, IList<string> warnings)
    {
        if (raw is not JsonObject root)
            throw new InvalidOperationException("v5 payload root must be a JSON object.");

        var showsArray = root["shows"] is JsonArray sa ? sa : new JsonArray();

        foreach (var showNode in showsArray)
        {
            if (showNode is not JsonObject showObj) continue;

            var showId = ParseGuid(showObj["id"]);
            var dateOfOpening = ParseDateOnly(showObj["dateOfOpening"]);

            // Add new ShowData scalars (enums serialized as integers)
            showObj["defaultScheduleMode"] = 0; // Traditional
            showObj["defaultAnchorEvent"] = 9;  // ON_STAGE
            showObj["breakTimeMinutes"] = 120;
            showObj["soundcheckGapMinutes"] = 0;

            // Leave new DateTime? scalars and meal-hour slots null (absent = null on deserialize)
            // venueOpenTime, venueCloseTime, technicalGetInTime, doorsOpeningTime,
            // firstShowTime, soundCurfewTime, backstageCurfewTime,
            // breakfastHours, lunchHours, dinnerHours

            // Clean StageLinkGroups
            var stageIds = new HashSet<int>();
            if (showObj["stages"] is JsonArray stages)
            {
                foreach (var stageNode in stages)
                {
                    if (stageNode is JsonObject stageObj)
                    {
                        var sid = ParseInt(stageObj["id"]);
                        stageIds.Add(sid);
                    }
                }
            }

            if (showObj["stageLinkGroups"] is JsonArray groups)
            {
                var cleaned = new JsonArray();
                foreach (var groupNode in groups)
                {
                    if (groupNode is not JsonObject groupObj) continue;
                    var idsArray = groupObj["stageIds"] is JsonArray ia ? ia : new JsonArray();
                    var kept = new JsonArray();
                    foreach (var idNode in idsArray)
                    {
                        var sid = ParseInt(idNode);
                        if (stageIds.Contains(sid))
                            kept.Add(sid);
                    }
                    if (kept.Count >= 2)
                    {
                        var clone = groupObj.DeepClone() as JsonObject ?? new JsonObject();
                        clone["stageIds"] = kept;
                        cleaned.Add(clone);
                    }
                }
                showObj["stageLinkGroups"] = cleaned;
            }
            else
            {
                showObj["stageLinkGroups"] = new JsonArray();
            }

            // Migrate running orders
            if (showObj["runningOrders"] is JsonArray roArray)
            {
                foreach (var roNode in roArray)
                {
                    if (roNode is not JsonObject roObj) continue;

                    var showDayNumber = Math.Max(1, ParseInt(roObj["showDayNumber"]));
                    var baseDate = dateOfOpening == default
                        ? DateTime.Today
                        : dateOfOpening.AddDays(showDayNumber - 1).ToDateTime(TimeOnly.MinValue);

                    // Add per-RO nullable overrides (absent = null on deserialize)
                    // modeOverride, anchorEventOverride, venueOpenTimeOverride,
                    // venueCloseTimeOverride, technicalGetInTimeOverride,
                    // doorsOpeningTimeOverride, firstShowTimeOverride,
                    // soundCurfewTimeOverride, backstageCurfewTimeOverride,
                    // breakfastHoursOverride, lunchHoursOverride, dinnerHoursOverride,
                    // breakTimeMinutesOverride, soundcheckGapMinutesOverride

                    // Add default VenueOptions
                    roObj["venueOptions"] = new JsonObject
                    {
                        ["includeGetIn"] = true,
                        ["includeLoadInVenue"] = false,
                        ["includeStageLoadIn"] = true,
                        ["includeBackstageDrop"] = false,
                        ["includeSetupOnStage"] = true,
                        ["includeSoundcheck"] = true,
                        ["includePreShowLinecheck"] = true,
                        ["defaultGetInMinutes"] = 15,
                        ["defaultLoadInVenueMinutes"] = 30,
                        ["defaultStageLoadInMinutes"] = 30,
                        ["defaultBackstageDropMinutes"] = 15,
                        ["defaultSetupOnStageMinutes"] = 15,
                        ["defaultSoundcheckMinutes"] = 30,
                        ["defaultPreShowLinecheckMinutes"] = 10,
                        ["defaultBackstageLeadMinutes"] = 15,
                        ["defaultChangeoverMinutes"] = 15,
                        ["defaultSetLengthMinutes"] = 45,
                    };

                    // festivalTemplate left null

                    // Migrate slots
                    if (roObj["slots"] is JsonArray slots)
                    {
                        var slotCount = slots.Count;
                        var playingIndex = 0;

                        for (int i = 0; i < slots.Count; i++)
                        {
                            if (slots[i] is not JsonObject slotObj) continue;

                            var slotId = Guid.NewGuid();
                            var bandId = ParseGuid(slotObj["bandId"]);
                            var stageId = ParseInt(slotObj["stageId"]);
                            var startTimeStr = ParseString(slotObj["startTime"]);
                            var setLength = ParseNullableInt(slotObj["setLengthMinutes"]);
                            var changeover = ParseNullableInt(slotObj["changeoverMinutes"]);
                            var notes = ParseString(slotObj["notes"]);

                            // Reconstruct OnStageTime from baseDate + StartTime
                            DateTime? onStageTime = null;
                            if (!string.IsNullOrEmpty(startTimeStr) &&
                                TimeOnly.TryParse(startTimeStr, CultureInfo.InvariantCulture, out var timeOnly))
                            {
                                onStageTime = baseDate.Date.Add(timeOnly.ToTimeSpan());
                            }

                            var soundcheckOrderIndex = (slotCount - 1) - playingIndex;
                            playingIndex++;

                            // Build PreShowEvents with CHANGEOVER (from v5 ChangeoverMinutes)
                            var preShowEvents = new JsonArray();
                            if (changeover is int co && co > 0)
                            {
                                preShowEvents.Add(new JsonObject
                                {
                                    ["eventType"] = 7, // CHANGEOVER
                                    ["startTime"] = null,
                                    ["durationMinutes"] = co,
                                    ["isPinned"] = false,
                                });
                            }
                            // Add PRESHOW_LINECHECK with null duration (default fallback)
                            preShowEvents.Add(new JsonObject
                            {
                                ["eventType"] = 8, // PRESHOW_LINECHECK
                                ["startTime"] = null,
                                ["durationMinutes"] = null,
                                ["isPinned"] = false,
                            });

                            // Build the new v6 slot object
                            var newSlot = new JsonObject
                            {
                                ["id"] = slotId.ToString(),
                                ["bandId"] = bandId == Guid.Empty ? null : bandId.ToString(),
                                ["stageId"] = stageId,
                                ["onStageTime"] = onStageTime.HasValue ? onStageTime.Value.ToString("O", CultureInfo.InvariantCulture) : null,
                                ["isOnStagePinned"] = true,
                                ["setLengthMinutes"] = setLength,
                                ["soundcheckOrderIndex"] = soundcheckOrderIndex,
                                ["earlyChain"] = new JsonArray(),
                                ["preShowEvents"] = preShowEvents,
                                ["postShowEvents"] = new JsonArray(),
                                ["backstageTime"] = null,
                                ["isBackstageTimePinned"] = false,
                                ["backstageLeadMinutes"] = null,
                                ["backstageCurfewTime"] = null,
                                ["isBackstageCurfewPinned"] = false,
                                ["cateringSlot"] = null,
                                ["flags"] = 0, // None
                                ["overrideFlags"] = 0, // None
                                ["notes"] = notes,
                            };

                            slots[i] = newSlot;
                        }
                    }
                }
            }
        }

        root["schemaVersion"] = 6;
        return root;
    }
}
