using System.Text.Json;
using FestivalRider.BundleMigrators;
using Xunit;

namespace FestivalRider.Tests.BundleMigrators;

public sealed class BundleScratchTests
{
    [Fact]
    public void ParseManifest_RoundTrips_AllValueShapes()
    {
        const string json = """
            {
              "format": "festivalrider-bundle",
              "schemaVersion": 2,
              "show": "show.csv",
              "bands": [ "bands/aaa.csv", "bands/bbb.csv" ],
              "runningOrders": [ "running-orders/ccc.csv" ]
            }
            """;

        var dict = BundleScratch.ParseManifest(json);
        var scratch = new BundleScratch(dict, new Dictionary<string, string>(), 2);

        Assert.Equal("festivalrider-bundle", BundleScratch.TryGetString(scratch.Manifest, "format"));
        Assert.Equal("show.csv", BundleScratch.TryGetString(scratch.Manifest, "show"));

        var roundTripped = scratch.SerializeManifest();
        // Re-deserialize to a fresh dictionary; assert key set is preserved.
        var second = BundleScratch.ParseManifest(roundTripped);
        Assert.Equal(
            new[] { "format", "schemaVersion", "show", "bands", "runningOrders" }.OrderBy(s => s).ToArray(),
            second.Keys.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void Constructor_RejectsNullArgs()
    {
        Assert.Throws<ArgumentNullException>(() => new BundleScratch(null!, new Dictionary<string, string>(), 2));
        Assert.Throws<ArgumentNullException>(() => new BundleScratch(new Dictionary<string, object?>(), null!, 2));
    }

    [Fact]
    public void TryGetString_HandlesJsonElementAndNativeString()
    {
        var dict = new Dictionary<string, object?>
        {
            ["fromString"] = "literal",
            ["fromJson"] = JsonDocument.Parse("\"json\"").RootElement,
            ["fromNumber"] = JsonDocument.Parse("42").RootElement,
            ["fromNull"] = null,
        };
        Assert.Equal("literal", BundleScratch.TryGetString(dict, "fromString"));
        Assert.Equal("json", BundleScratch.TryGetString(dict, "fromJson"));
        Assert.Null(BundleScratch.TryGetString(dict, "fromNumber"));
        Assert.Null(BundleScratch.TryGetString(dict, "fromNull"));
        Assert.Null(BundleScratch.TryGetString(dict, "missing"));
    }

    [Fact]
    public void Manifest_PropertyBag_AllowsHeterogeneousWrites()
    {
        var dict = BundleScratch.ParseManifest("""{ "schemaVersion": 2 }""");
        var scratch = new BundleScratch(dict, new Dictionary<string, string>(), 2);
        scratch.Manifest["shows"] = new List<object?> { "shows/x.csv" };
        scratch.Manifest["activeShowId"] = Guid.Empty.ToString();
        scratch.Manifest["schemaVersion"] = 3;

        var json = scratch.SerializeManifest();
        var roundTripped = BundleScratch.ParseManifest(json);
        Assert.Contains("shows", roundTripped.Keys);
        Assert.Contains("activeShowId", roundTripped.Keys);
    }
}
