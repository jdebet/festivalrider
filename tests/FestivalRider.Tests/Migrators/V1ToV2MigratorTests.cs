using System.Text.Json.Nodes;
using FestivalRider.Migrators;
using Xunit;

namespace FestivalRider.Tests.Migrators;

public sealed class V1ToV2MigratorTests
{
    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void Reports_FromVersion_1_To_2()
    {
        var sut = new V1ToV2Migrator();
        Assert.Equal(1, sut.FromVersion);
        Assert.Equal(2, sut.ToVersion);
    }

    [Fact]
    public void Migrate_Minimal_v1_AddsShowData_PreservesBands()
    {
        var sut = new V1ToV2Migrator();
        var input = Parse("""
            {
              "schemaVersion": 1,
              "bands": [
                { "id": "11111111-1111-1111-1111-111111111111", "name": "Alpha" }
              ]
            }
            """);
        var warnings = new List<string>();

        var output = (JsonObject)sut.Migrate(input, warnings);

        Assert.NotNull(output["showData"]);
        Assert.NotNull(output["runningOrders"]);
        var bands = (JsonArray)output["bands"]!;
        Assert.Single(bands);
        Assert.Equal("Alpha", (string?)bands[0]!["name"]);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Migrate_DropsGenre_InputChannels_AndBackline_WithCounts()
    {
        var sut = new V1ToV2Migrator();
        var input = Parse("""
            {
              "schemaVersion": 1,
              "bands": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "name": "Alpha",
                  "genre": "Rock",
                  "rider": {
                    "tech": {
                      "inputs": [
                        { "channel": 1, "source": "Kick" },
                        { "channel": 2, "source": "Snare" }
                      ],
                      "backlineItems": [ { "name": "Amp" } ],
                      "backlineNotes": "spare cabs"
                    }
                  }
                },
                {
                  "id": "22222222-2222-2222-2222-222222222222",
                  "name": "Beta",
                  "genre": "Jazz"
                }
              ]
            }
            """);
        var warnings = new List<string>();

        var output = (JsonObject)sut.Migrate(input, warnings);

        // genre dropped on both bands
        var bands = (JsonArray)output["bands"]!;
        Assert.All(bands, b => Assert.Null(b!["genre"]));

        // tech.inputs and tech.backline* dropped on Alpha
        var alpha = (JsonObject)bands[0]!;
        var tech = (JsonObject)alpha["rider"]!["tech"]!;
        Assert.Null(tech["inputs"]);
        Assert.Null(tech["backlineItems"]);
        Assert.Null(tech["backlineNotes"]);

        // warnings record counts
        Assert.Contains(warnings, w => w.Contains("Band.Genre") && w.Contains("2"));
        Assert.Contains(warnings, w => w.Contains("Inputs") && w.Contains("2"));
        Assert.Contains(warnings, w => w.Contains("Backline") && w.Contains("2"));
    }

    [Fact]
    public void Migrate_PreservesExistingRunningOrders()
    {
        var sut = new V1ToV2Migrator();
        var input = Parse("""
            {
              "schemaVersion": 1,
              "bands": [],
              "runningOrders": [
                { "id": "33333333-3333-3333-3333-333333333333", "showDayNumber": 1, "slots": [] }
              ]
            }
            """);
        var warnings = new List<string>();

        var output = (JsonObject)sut.Migrate(input, warnings);

        var ros = (JsonArray)output["runningOrders"]!;
        Assert.Single(ros);
        Assert.Equal("33333333-3333-3333-3333-333333333333", (string?)ros[0]!["id"]);
    }

    [Fact]
    public void Migrate_NonObjectRoot_Throws()
    {
        var sut = new V1ToV2Migrator();
        var input = JsonNode.Parse("[]")!;
        Assert.Throws<InvalidOperationException>(() => sut.Migrate(input, new List<string>()));
    }

    [Fact]
    public void Migrate_IsIdempotentWhenReRunOnAlreadyMigratedShape()
    {
        // After v1->v2 the migrator's output has no genre/inputs/backline*; running it again
        // produces an identical payload and zero warnings.
        var sut = new V1ToV2Migrator();
        var first = (JsonObject)sut.Migrate(Parse("""
            {
              "schemaVersion": 1,
              "bands": [ { "id": "11111111-1111-1111-1111-111111111111", "name": "Alpha", "genre": "Rock" } ]
            }
            """), new List<string>());

        var warnings = new List<string>();
        var second = (JsonObject)sut.Migrate(JsonNode.Parse(first.ToJsonString())!, warnings);

        Assert.Empty(warnings);
        Assert.Equal(first.ToJsonString(), second.ToJsonString());
    }
}
