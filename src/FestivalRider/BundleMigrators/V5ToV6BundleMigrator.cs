using System.Text;

namespace FestivalRider.BundleMigrators;

// Plan 020. Rewrites v5 running-order CSVs to the v6 column list.
//
// Manifest:
//   - bumps schemaVersion 5 -> 6 (single-show shape unchanged).
//
// Entries:
//   - rewrites every running-orders/{Guid}.csv from
//     ShowId,Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes
//     to the v6 23-column list with defaults.
//   - mints a fresh Guid Id per row.
//   - StartTime -> OnStageTime with OnStageDayOffset=0, IsOnStagePinned=true.
//   - SoundcheckOrderIndex computed by row position (reverse playing order).
//   - ChangeoverMinutes column is dropped (v5 value does not survive CSV).
//
// FROZEN ON SHIP. Bug fixes land as a successor migrator.
public sealed class V5ToV6BundleMigrator : IBundleMigrator
{
    public int FromVersion => 5;
    public int ToVersion => 6;

    public void Migrate(BundleScratch scratch, IList<string> warnings)
    {
        if (scratch is null) throw new ArgumentNullException(nameof(scratch));
        if (warnings is null) throw new ArgumentNullException(nameof(warnings));

        var roPaths = scratch.Entries.Keys
            .Where(k => k.StartsWith("running-orders/", StringComparison.Ordinal))
            .ToList();

        foreach (var path in roPaths)
        {
            scratch.Entries[path] = MigrateRunningOrderCsv(scratch.Entries[path]);
        }
    }

    internal static string MigrateRunningOrderCsv(string csv)
    {
        if (string.IsNullOrEmpty(csv)) return csv;
        var rows = SplitCsvRows(csv).ToList();
        if (rows.Count == 0) return csv;

        var sb = new StringBuilder();
        sb.Append(
            "Id,ShowId,BandName,Stage,OnStageTime,OnStageDayOffset,IsOnStagePinned,SetLengthMinutes,SoundcheckOrderIndex," +
            "BackstageTime,BackstageDayOffset,IsBackstageTimePinned,BackstageLeadMinutes,BackstageCurfewTime,BackstageCurfewDayOffset,IsBackstageCurfewPinned," +
            "CateringStart,CateringStartDayOffset,CateringEnd,CateringEndDayOffset,Flags,OverrideFlags,Notes")
            .Append('\n');

        int dataRowCount = 0;
        for (int i = 1; i < rows.Count; i++)
        {
            if (!string.IsNullOrEmpty(rows[i])) dataRowCount++;
        }

        int dataIndex = 0;
        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (string.IsNullOrEmpty(row))
            {
                sb.Append('\n');
                continue;
            }

            var parts = SplitCsvFields(row);
            // v5 columns: ShowId,Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes
            string showId = Get(parts, 0);
            string stage = Get(parts, 1);
            string startTime = Get(parts, 2);
            string bandName = Get(parts, 3);
            string setLength = Get(parts, 4);
            string notes = Get(parts, 6);

            string id = Guid.NewGuid().ToString();
            int soundcheckOrderIndex = (dataRowCount - 1) - dataIndex;
            dataIndex++;

            // Write v6 columns
            sb.Append(Escape(id)).Append(',');
            sb.Append(Escape(showId)).Append(',');
            sb.Append(Escape(bandName)).Append(',');
            sb.Append(Escape(stage)).Append(',');
            sb.Append(Escape(startTime)).Append(','); // OnStageTime
            sb.Append('0').Append(','); // OnStageDayOffset
            sb.Append("true").Append(','); // IsOnStagePinned
            sb.Append(Escape(setLength)).Append(',');
            sb.Append(Inv(soundcheckOrderIndex)).Append(',');
            sb.Append(','); // BackstageTime
            sb.Append(','); // BackstageDayOffset
            sb.Append(','); // IsBackstageTimePinned
            sb.Append(','); // BackstageLeadMinutes
            sb.Append(','); // BackstageCurfewTime
            sb.Append(','); // BackstageCurfewDayOffset
            sb.Append(','); // IsBackstageCurfewPinned
            sb.Append(','); // CateringStart
            sb.Append(','); // CateringStartDayOffset
            sb.Append(','); // CateringEnd
            sb.Append(','); // CateringEndDayOffset
            sb.Append(','); // Flags
            sb.Append(','); // OverrideFlags
            sb.Append(Escape(notes)).Append('\n');
        }

        return sb.ToString();
    }

    private static string Inv(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Get(List<string> parts, int index) =>
        index < parts.Count ? parts[index] : string.Empty;

    private static List<string> SplitCsvFields(string row)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < row.Length; i++)
        {
            char c = row[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < row.Length && row[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private static IEnumerable<string> SplitCsvRows(string csv)
    {
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (c == '"')
            {
                sb.Append(c);
                if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    sb.Append(csv[++i]);
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == '\n' && !inQuotes)
            {
                yield return sb.ToString();
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
