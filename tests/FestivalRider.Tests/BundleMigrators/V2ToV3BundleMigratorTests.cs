using FestivalRider.BundleMigrators;
using Xunit;

namespace FestivalRider.Tests.BundleMigrators;

public sealed class V2ToV3BundleMigratorTests
{
    private const string Header = "Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes";

    [Fact]
    public void Reports_FromVersion_2_To_3()
    {
        var sut = new V2ToV3BundleMigrator();
        Assert.Equal(2, sut.FromVersion);
        Assert.Equal(3, sut.ToVersion);
    }

    private static BundleScratch BuildScratch(string manifestJson, params (string name, string content)[] entries)
    {
        var dict = BundleScratch.ParseManifest(manifestJson);
        var entryMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, content) in entries)
            entryMap[name] = content;
        return new BundleScratch(dict, entryMap, 2);
    }

    [Fact]
    public void Migrate_RewritesManifest_RenamesShowEntry_AddsActiveShowId()
    {
        var sut = new V2ToV3BundleMigrator();
        var scratch = BuildScratch(
            """{ "schemaVersion": 2, "show": "show.csv", "bands": [], "runningOrders": [] }""",
            ("show.csv", "Section,Key,Value,Index,Notes\nShow,Name,Festival,,\n"));

        var warnings = new List<string>();
        sut.Migrate(scratch, warnings);

        Assert.False(scratch.Manifest.ContainsKey("show"));
        Assert.True(scratch.Manifest.ContainsKey("shows"));
        Assert.True(scratch.Manifest.ContainsKey("activeShowId"));
        var activeIdStr = BundleScratch.TryGetString(scratch.Manifest, "activeShowId");
        Assert.True(Guid.TryParse(activeIdStr, out var newId) && newId != Guid.Empty);

        Assert.False(scratch.Entries.ContainsKey("show.csv"));
        var newPath = $"shows/{newId}.csv";
        Assert.True(scratch.Entries.ContainsKey(newPath));
        Assert.Contains("Festival", scratch.Entries[newPath]);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Migrate_PrependsShowIdColumn_ToRunningOrders()
    {
        var sut = new V2ToV3BundleMigrator();
        const string roCsv =
            "Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes\n" +
            "Main,18:00,Alpha,60,15,Headliner\n" +
            "Acoustic,14:00,Beta,30,10,Warmup\n";
        var scratch = BuildScratch(
            """{ "schemaVersion": 2, "show": "show.csv", "bands": [], "runningOrders": ["running-orders/aaa.csv"] }""",
            ("show.csv", "Section,Key\n"),
            ("running-orders/aaa.csv", roCsv));

        sut.Migrate(scratch, new List<string>());

        var newId = Guid.Parse(BundleScratch.TryGetString(scratch.Manifest, "activeShowId")!);
        var migrated = scratch.Entries["running-orders/aaa.csv"];
        var lines = migrated.Split('\n');
        Assert.Equal("ShowId," + Header, lines[0]);
        Assert.StartsWith($"{newId},Main,18:00,Alpha", lines[1]);
        Assert.StartsWith($"{newId},Acoustic,14:00,Beta", lines[2]);
    }

    [Fact]
    public void Migrate_HeaderOnlyRunningOrder_PrependsHeaderOnly()
    {
        var sut = new V2ToV3BundleMigrator();
        var scratch = BuildScratch(
            """{ "schemaVersion": 2, "show": "show.csv", "bands": [], "runningOrders": ["running-orders/empty.csv"] }""",
            ("show.csv", "x"),
            ("running-orders/empty.csv", Header + "\n"));

        sut.Migrate(scratch, new List<string>());

        Assert.Equal("ShowId," + Header + "\n", scratch.Entries["running-orders/empty.csv"]);
    }

    [Fact]
    public void Migrate_QuotedFieldWithEmbeddedNewline_IsPreserved()
    {
        var sut = new V2ToV3BundleMigrator();
        // Notes column contains a quoted newline; the migrator must not split on it.
        const string roCsv =
            "Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes\n" +
            "Main,18:00,Alpha,60,15,\"line1\nline2\"\n";
        var scratch = BuildScratch(
            """{ "schemaVersion": 2, "show": "show.csv", "bands": [], "runningOrders": ["running-orders/q.csv"] }""",
            ("show.csv", "x"),
            ("running-orders/q.csv", roCsv));

        sut.Migrate(scratch, new List<string>());
        var migrated = scratch.Entries["running-orders/q.csv"];

        // Exactly two output rows: header + one data row.
        var rows = SplitCsvRowsForTest(migrated).ToList();
        Assert.Equal(2, rows.Count);
        Assert.StartsWith("ShowId,Stage", rows[0]);
        Assert.Contains("\"line1\nline2\"", rows[1]);
    }

    [Fact]
    public void Migrate_EscapedDoubleQuoteInsideQuotedField_IsPreserved()
    {
        var sut = new V2ToV3BundleMigrator();
        const string roCsv =
            "Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes\n" +
            "Main,18:00,Alpha,60,15,\"says \"\"hi\"\"\"\n";
        var scratch = BuildScratch(
            """{ "schemaVersion": 2, "show": "show.csv", "bands": [], "runningOrders": ["running-orders/eq.csv"] }""",
            ("show.csv", "x"),
            ("running-orders/eq.csv", roCsv));

        sut.Migrate(scratch, new List<string>());
        var migrated = scratch.Entries["running-orders/eq.csv"];
        Assert.Contains("\"says \"\"hi\"\"\"", migrated);
        Assert.Equal(2, SplitCsvRowsForTest(migrated).Count());
    }

    [Fact]
    public void Migrate_MissingShowEntry_Warns()
    {
        var sut = new V2ToV3BundleMigrator();
        var scratch = BuildScratch(
            """{ "schemaVersion": 2, "show": "show.csv", "bands": [], "runningOrders": [] }"""
            // no show.csv entry
        );
        var warnings = new List<string>();
        sut.Migrate(scratch, warnings);
        Assert.Contains(warnings, w => w.Contains("missing show entry", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Migrate_MintsFreshShowId_OnEachInvocation()
    {
        var sut = new V2ToV3BundleMigrator();
        var s1 = BuildScratch("""{ "schemaVersion": 2, "show": "show.csv" }""", ("show.csv", "x"));
        var s2 = BuildScratch("""{ "schemaVersion": 2, "show": "show.csv" }""", ("show.csv", "x"));
        sut.Migrate(s1, new List<string>());
        sut.Migrate(s2, new List<string>());
        Assert.NotEqual(
            BundleScratch.TryGetString(s1.Manifest, "activeShowId"),
            BundleScratch.TryGetString(s2.Manifest, "activeShowId"));
    }

    [Fact]
    public void Migrate_NullArgs_Throw()
    {
        var sut = new V2ToV3BundleMigrator();
        Assert.Throws<ArgumentNullException>(() => sut.Migrate(null!, new List<string>()));
        var scratch = BuildScratch("""{ "schemaVersion": 2 }""");
        Assert.Throws<ArgumentNullException>(() => sut.Migrate(scratch, null!));
    }

    // Local CSV row splitter for assertions; mirrors the migrator's logic so we
    // can count rows in outputs containing quoted newlines.
    private static IEnumerable<string> SplitCsvRowsForTest(string csv)
    {
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (c == '"')
            {
                sb.Append(c);
                if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"') { sb.Append(csv[++i]); }
                else inQuotes = !inQuotes;
            }
            else if (c == '\n' && !inQuotes) { yield return sb.ToString(); sb.Clear(); }
            else sb.Append(c);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
