using System.Text.Json.Nodes;
using FestivalRider.Migrators;
using Xunit;

namespace FestivalRider.Tests.Migrators;

public sealed class V2ToV3MigratorTests
{
    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void Reports_FromVersion_2_To_3()
    {
        var sut = new V2ToV3Migrator();
        Assert.Equal(2, sut.FromVersion);
        Assert.Equal(3, sut.ToVersion);
    }

    [Fact]
    public void Migrate_WrapsShowDataIntoShowsList_AndSetsActiveShowId()
    {
        var sut = new V2ToV3Migrator();
        var input = Parse("""
            {
              "schemaVersion": 2,
              "bands": [],
              "showData": {
                "id": "44444444-4444-4444-4444-444444444444",
                "name": "Festival",
                "address": "Main",
                "dateOfOpening": "2024-06-15",
                "showDayCount": 3,
                "stages": [ { "id": 1, "name": "Main" } ]
              },
              "runningOrders": []
            }
            """);
        var warnings = new List<string>();

        var output = (JsonObject)sut.Migrate(input, warnings);

        Assert.Null(output["showData"]);
        var shows = (JsonArray)output["shows"]!;
        Assert.Single(shows);
        Assert.Equal("Festival", (string?)shows[0]!["name"]);
        Assert.Equal("44444444-4444-4444-4444-444444444444", (string?)shows[0]!["id"]);
        Assert.Equal("44444444-4444-4444-4444-444444444444", (string?)output["activeShowId"]);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Migrate_MintsShowId_WhenMissing()
    {
        var sut = new V2ToV3Migrator();
        var input = Parse("""
            {
              "schemaVersion": 2,
              "showData": { "name": "Festival", "stages": [] }
            }
            """);

        var output = (JsonObject)sut.Migrate(input, new List<string>());

        var idStr = (string?)((JsonArray)output["shows"]!)[0]!["id"];
        Assert.True(Guid.TryParse(idStr, out var id) && id != Guid.Empty);
        Assert.Equal(idStr, (string?)output["activeShowId"]);
    }

    [Fact]
    public void Migrate_StampsRunningOrdersWithShowId_AndCounts()
    {
        var sut = new V2ToV3Migrator();
        var input = Parse("""
            {
              "schemaVersion": 2,
              "showData": {
                "id": "44444444-4444-4444-4444-444444444444",
                "name": "Festival",
                "stages": []
              },
              "runningOrders": [
                { "id": "11111111-1111-1111-1111-111111111111", "showDayNumber": 1, "slots": [] },
                { "id": "22222222-2222-2222-2222-222222222222", "showDayNumber": 2, "slots": [] }
              ]
            }
            """);
        var warnings = new List<string>();

        var output = (JsonObject)sut.Migrate(input, warnings);

        var ros = (JsonArray)output["runningOrders"]!;
        Assert.All(ros, r => Assert.Equal("44444444-4444-4444-4444-444444444444", (string?)r!["showId"]));
        Assert.Contains(warnings, w => w.Contains("2 running order"));
    }

    [Fact]
    public void Migrate_PreservesExistingNonEmptyShowId()
    {
        var sut = new V2ToV3Migrator();
        var input = Parse("""
            {
              "schemaVersion": 2,
              "showData": { "id": "44444444-4444-4444-4444-444444444444", "name": "F", "stages": [] },
              "runningOrders": [
                { "id": "55555555-5555-5555-5555-555555555555", "showId": "66666666-6666-6666-6666-666666666666", "showDayNumber": 1, "slots": [] }
              ]
            }
            """);
        var warnings = new List<string>();

        var output = (JsonObject)sut.Migrate(input, warnings);

        var ros = (JsonArray)output["runningOrders"]!;
        Assert.Equal("66666666-6666-6666-6666-666666666666", (string?)ros[0]!["showId"]);
        Assert.DoesNotContain(warnings, w => w.Contains("running order"));
    }

    [Fact]
    public void Migrate_NoShowData_SeedsDefaultAndWarns()
    {
        var sut = new V2ToV3Migrator();
        var input = Parse("""{ "schemaVersion": 2, "bands": [] }""");
        var warnings = new List<string>();

        var output = (JsonObject)sut.Migrate(input, warnings);

        Assert.Single((JsonArray)output["shows"]!);
        Assert.NotNull(output["activeShowId"]);
        Assert.Contains(warnings, w => w.Contains("seeded a default show"));
    }

    [Fact]
    public void Migrate_NonObjectRoot_Throws()
    {
        var sut = new V2ToV3Migrator();
        Assert.Throws<InvalidOperationException>(() => sut.Migrate(JsonNode.Parse("[]")!, new List<string>()));
    }
}
