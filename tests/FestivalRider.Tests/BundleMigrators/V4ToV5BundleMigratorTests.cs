using FestivalRider.BundleMigrators;
using Xunit;

namespace FestivalRider.Tests.BundleMigrators;

public sealed class V4ToV5BundleMigratorTests
{
    [Fact]
    public void Reports_FromVersion_4_To_5()
    {
        var sut = new V4ToV5BundleMigrator();
        Assert.Equal(4, sut.FromVersion);
        Assert.Equal(5, sut.ToVersion);
    }

    private static BundleScratch BuildScratch(string manifestJson, params (string name, string content)[] entries)
    {
        var dict = BundleScratch.ParseManifest(manifestJson);
        var entryMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, content) in entries)
            entryMap[name] = content;
        return new BundleScratch(dict, entryMap, 4);
    }

    [Fact]
    public void Migrate_KeepsActiveShowRunningOrders()
    {
        var activeId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var roCsv =
            "ShowId,Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes\n" +
            $"{activeId},Main,18:00,Alpha,60,15,Headliner\n";

        var scratch = BuildScratch(
            $"{{ \"schemaVersion\": 4, \"activeShowId\": \"{activeId}\", \"shows\": [\"shows/{activeId}.csv\"], \"bands\": [], \"runningOrders\": [\"running-orders/ro.csv\"] }}",
            ($"shows/{activeId}.csv", "Section,Key\nShow,Name,Festival\n"),
            ("running-orders/ro.csv", roCsv));

        var warnings = new List<string>();
        var sut = new V4ToV5BundleMigrator();
        sut.Migrate(scratch, warnings);

        Assert.Contains("running-orders/ro.csv", scratch.Entries.Keys);
        Assert.Contains("running-orders/ro.csv", Assert.IsType<List<string>>(scratch.Manifest["runningOrders"])!);
        Assert.DoesNotContain(warnings, w => w.Contains("dropped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Migrate_DropsNonActiveRunningOrders_AndWarns()
    {
        var activeId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var otherId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var activeRoCsv =
            "ShowId,Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes\n" +
            $"{activeId},Main,18:00,Alpha,60,15,Headliner\n";
        var otherRoCsv =
            "ShowId,Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes\n" +
            $"{otherId},Acoustic,14:00,Beta,30,10,Warmup\n";

        var scratch = BuildScratch(
            $"{{ \"schemaVersion\": 4, \"activeShowId\": \"{activeId}\", \"shows\": [\"shows/{activeId}.csv\"], \"bands\": [], \"runningOrders\": [\"running-orders/active.csv\", \"running-orders/other.csv\"] }}",
            ($"shows/{activeId}.csv", "Section,Key\nShow,Name,Festival\n"),
            ("running-orders/active.csv", activeRoCsv),
            ("running-orders/other.csv", otherRoCsv));

        var warnings = new List<string>();
        var sut = new V4ToV5BundleMigrator();
        sut.Migrate(scratch, warnings);

        Assert.Contains("running-orders/active.csv", scratch.Entries.Keys);
        Assert.DoesNotContain("running-orders/other.csv", scratch.Entries.Keys);
        Assert.Contains(warnings, w => w.Contains("dropped", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, w => w.Contains(otherId.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void Migrate_NullArgs_Throw()
    {
        var sut = new V4ToV5BundleMigrator();
        Assert.Throws<ArgumentNullException>(() => sut.Migrate(null!, new List<string>()));
        var scratch = BuildScratch("""{ "schemaVersion": 4 }""");
        Assert.Throws<ArgumentNullException>(() => sut.Migrate(scratch, null!));
    }
}
