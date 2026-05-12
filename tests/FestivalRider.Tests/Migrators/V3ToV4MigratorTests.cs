using System.Text.Json.Nodes;
using FestivalRider.Migrators;
using Xunit;

namespace FestivalRider.Tests.Migrators;

public sealed class V3ToV4MigratorTests
{
    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void Reports_FromVersion_3_To_4()
    {
        var sut = new V3ToV4Migrator();
        Assert.Equal(3, sut.FromVersion);
        Assert.Equal(4, sut.ToVersion);
    }

    [Fact]
    public void Migrate_ConvertsBoolRoundTrip_ToIntCount()
    {
        var sut = new V3ToV4Migrator();
        var input = Parse("""
            {
              "schemaVersion": 3,
              "bands": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "name": "Alpha",
                  "rider": {
                    "tech": {
                      "foh": {
                        "stageToFohRoundTrip": true,
                        "stageToFohSendCount": 16
                      }
                    }
                  }
                }
              ]
            }
            """);

        var output = (JsonObject)sut.Migrate(input, new List<string>());
        var foh = (JsonObject)((JsonObject)((JsonObject)((JsonArray)output["bands"]!)[0]!["rider"]!)["tech"]!)["foh"]!;

        Assert.Null(foh["stageToFohRoundTrip"]);
        Assert.Equal(1, (int?)foh["stageToFohRoundTripCount"]);
    }

    [Fact]
    public void Migrate_FalseRoundTrip_BecomesZero()
    {
        var sut = new V3ToV4Migrator();
        var input = Parse("""
            {
              "schemaVersion": 3,
              "bands": [
                {
                  "rider": {
                    "tech": {
                      "foh": {
                        "stageToFohRoundTrip": false
                      }
                    }
                  }
                }
              ]
            }
            """);

        var output = (JsonObject)sut.Migrate(input, new List<string>());
        var foh = (JsonObject)((JsonObject)((JsonObject)((JsonArray)output["bands"]!)[0]!["rider"]!)["tech"]!)["foh"]!;

        Assert.Null(foh["stageToFohRoundTrip"]);
        Assert.Equal(0, (int?)foh["stageToFohRoundTripCount"]);
    }

    [Fact]
    public void Migrate_AddsMissingOtherNulls_AndRoundTripCountZero()
    {
        var sut = new V3ToV4Migrator();
        var input = Parse("""
            {
              "schemaVersion": 3,
              "bands": [
                {
                  "rider": {
                    "tech": {
                      "foh": {}
                    }
                  }
                }
              ]
            }
            """);

        var output = (JsonObject)sut.Migrate(input, new List<string>());
        var foh = (JsonObject)((JsonObject)((JsonObject)((JsonArray)output["bands"]!)[0]!["rider"]!)["tech"]!)["foh"]!;

        Assert.Null(foh["outputProtocolOther"]);
        Assert.Null(foh["outputLocationOther"]);
        Assert.Equal(0, (int?)foh["stageToFohRoundTripCount"]);
    }

    [Fact]
    public void Migrate_SeedsCableMaxLength_AndMachineLocation()
    {
        var sut = new V3ToV4Migrator();
        var input = Parse("""
            {
              "schemaVersion": 3,
              "bands": [
                {
                  "rider": {
                    "tech": {
                      "cables": [ { "source": "SoundFoh", "target": "StageCenter" } ],
                      "lighting": {
                        "floorMachines": [ { "name": "Par", "count": 2 } ]
                      }
                    }
                  }
                }
              ]
            }
            """);

        var output = (JsonObject)sut.Migrate(input, new List<string>());
        var band = (JsonObject)((JsonArray)output["bands"]!)[0]!;
        var tech = (JsonObject)band["rider"]!["tech"]!;
        var cable = (JsonObject)((JsonArray)tech["cables"]!)[0]!;
        var machine = (JsonObject)((JsonArray)((JsonObject)tech["lighting"]!)["floorMachines"]!)[0]!;

        Assert.Null(cable["maxLengthMeters"]);
        Assert.Null(machine["location"]);
    }

    [Fact]
    public void Migrate_NonObjectRoot_Throws()
    {
        var sut = new V3ToV4Migrator();
        Assert.Throws<InvalidOperationException>(() => sut.Migrate(JsonNode.Parse("[]")!, new List<string>()));
    }
}
