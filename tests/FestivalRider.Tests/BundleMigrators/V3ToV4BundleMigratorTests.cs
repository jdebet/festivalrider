using FestivalRider.BundleMigrators;
using Xunit;

namespace FestivalRider.Tests.BundleMigrators;

public sealed class V3ToV4BundleMigratorTests
{
    [Fact]
    public void Reports_FromVersion_3_To_4()
    {
        var sut = new V3ToV4BundleMigrator();
        Assert.Equal(3, sut.FromVersion);
        Assert.Equal(4, sut.ToVersion);
    }

    private static BundleScratch BuildScratch(string manifestJson, params (string name, string content)[] entries)
    {
        var dict = BundleScratch.ParseManifest(manifestJson);
        var entryMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, content) in entries)
            entryMap[name] = content;
        return new BundleScratch(dict, entryMap, 3);
    }

    [Fact]
    public void Migrate_RenamesStageToFohRoundTrip_ToCount()
    {
        var sut = new V3ToV4BundleMigrator();
        var bandCsv =
            "Section,Key,Value,Index,Notes\n" +
            "Band,Id,aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa,,\n" +
            "Tech.Foh,StageToFohRoundTrip,True,,\n";

        var scratch = BuildScratch(
            """{ "schemaVersion": 3, "bands": ["bands/aaa.csv"], "runningOrders": [] }""",
            ("bands/aaa.csv", bandCsv));

        sut.Migrate(scratch, new List<string>());
        var migrated = scratch.Entries["bands/aaa.csv"];
        Assert.Contains("Tech.Foh,StageToFohRoundTripCount,1,,", migrated);
        Assert.DoesNotContain("StageToFohRoundTrip,True", migrated);
    }

    [Fact]
    public void Migrate_ConvertsFalseToZero()
    {
        var sut = new V3ToV4BundleMigrator();
        var bandCsv =
            "Section,Key,Value,Index,Notes\n" +
            "Tech.Foh,StageToFohRoundTrip,False,,\n";

        var scratch = BuildScratch(
            """{ "schemaVersion": 3, "bands": ["bands/b.csv"], "runningOrders": [] }""",
            ("bands/b.csv", bandCsv));

        sut.Migrate(scratch, new List<string>());
        var migrated = scratch.Entries["bands/b.csv"];
        Assert.Contains("Tech.Foh,StageToFohRoundTripCount,0,,", migrated);
    }

    [Fact]
    public void Migrate_AddsMissingOtherFields()
    {
        var sut = new V3ToV4BundleMigrator();
        var bandCsv =
            "Section,Key,Value,Index,Notes\n" +
            "Band,Name,Alpha,,\n" +
            "Tech.Foh,OutputProtocol,Aes,,\n";

        var scratch = BuildScratch(
            """{ "schemaVersion": 3, "bands": ["bands/c.csv"], "runningOrders": [] }""",
            ("bands/c.csv", bandCsv));

        sut.Migrate(scratch, new List<string>());
        var migrated = scratch.Entries["bands/c.csv"];
        Assert.Contains("Tech.Foh,OutputProtocolOther,,,", migrated);
        Assert.Contains("Tech.Foh,OutputLocationOther,,,", migrated);
    }

    [Fact]
    public void Migrate_AddsMaxLength_AndLocationRows()
    {
        var sut = new V3ToV4BundleMigrator();
        var bandCsv =
            "Section,Key,Value,Index,Notes\n" +
            "Tech.Cable,MinLengthMeters,15,0,\n" +
            "Tech.LightingMachine,Name,Par,0,\n" +
            "Tech.LightingMachine,Count,2,0,\n";

        var scratch = BuildScratch(
            """{ "schemaVersion": 3, "bands": ["bands/d.csv"], "runningOrders": [] }""",
            ("bands/d.csv", bandCsv));

        sut.Migrate(scratch, new List<string>());
        var migrated = scratch.Entries["bands/d.csv"];
        Assert.Contains("Tech.Cable,MaxLengthMeters,,0,", migrated);
        Assert.Contains("Tech.LightingMachine,Location,,0,", migrated);
    }

    [Fact]
    public void Migrate_NullArgs_Throw()
    {
        var sut = new V3ToV4BundleMigrator();
        Assert.Throws<ArgumentNullException>(() => sut.Migrate(null!, new List<string>()));
        var scratch = BuildScratch("""{ "schemaVersion": 3 }""");
        Assert.Throws<ArgumentNullException>(() => sut.Migrate(scratch, null!));
    }
}
