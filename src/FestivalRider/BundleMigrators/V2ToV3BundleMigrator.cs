using System.Text;

namespace FestivalRider.BundleMigrators;

// Plan 013. Maps a v2 single-show bundle layout onto v3 multi-show.
//
// Manifest:
//   - rewrites `show: "show.csv"` into `shows: ["shows/{newId}.csv"]`
//     and `activeShowId: "{newId}"`.
//   - leaves `bands` and `runningOrders` untouched (paths unchanged).
//
// Entries:
//   - renames the `show.csv` entry to `shows/{newId}.csv`.
//   - prepends a `ShowId` column (and the new id on every data row) to every
//     `running-orders/{Guid}.csv` entry, matching the v3 column order
//     `ShowId,Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes`.
//
// `newId` is minted fresh because v2 manifests carried no show id. Two imports
// of the same v2 bundle therefore produce two different shows on the receiver;
// the user is expected to import a given v2 bundle exactly once.
//
// FROZEN ON SHIP. Bug fixes land as a successor migrator.
public sealed class V2ToV3BundleMigrator : IBundleMigrator
{
    public int FromVersion => 2;
    public int ToVersion => 3;

    public void Migrate(BundleScratch scratch, IList<string> warnings)
    {
        if (scratch is null) throw new ArgumentNullException(nameof(scratch));
        if (warnings is null) throw new ArgumentNullException(nameof(warnings));

        var newId = Guid.NewGuid();
        var newShowPath = $"shows/{newId}.csv";

        var v2ShowPath = BundleScratch.TryGetString(scratch.Manifest, "show") ?? "show.csv";

        if (scratch.Entries.TryGetValue(v2ShowPath, out var showText))
        {
            scratch.Entries.Remove(v2ShowPath);
            scratch.Entries[newShowPath] = showText;
        }
        else
        {
            warnings.Add(
                $"v2 bundle missing show entry \"{v2ShowPath}\"; new manifest still references {newShowPath}.");
        }

        scratch.Manifest.Remove("show");
        scratch.Manifest["shows"] = new List<object?> { newShowPath };
        scratch.Manifest["activeShowId"] = newId.ToString();

        var roPaths = scratch.Entries.Keys
            .Where(k => k.StartsWith("running-orders/", StringComparison.Ordinal))
            .ToList();
        foreach (var path in roPaths)
        {
            scratch.Entries[path] = PrependShowIdColumn(scratch.Entries[path], newId);
        }
    }

    // Prepend `ShowId,` to the header and `{showId},` to every data row. Walks
    // the CSV character-by-character so that fields containing newlines inside
    // quotes don't fool a naive line splitter (CsvHelper escapes those as
    // `"..."` runs with embedded `\n`).
    internal static string PrependShowIdColumn(string csv, Guid showId)
    {
        if (string.IsNullOrEmpty(csv)) return csv;
        var rows = SplitCsvRows(csv).ToList();
        if (rows.Count == 0) return csv;

        var sb = new StringBuilder();
        sb.Append("ShowId,").Append(rows[0]).Append('\n');
        var idStr = showId.ToString();
        for (int i = 1; i < rows.Count; i++)
        {
            if (rows[i].Length == 0)
            {
                sb.Append('\n');
                continue;
            }
            sb.Append(idStr).Append(',').Append(rows[i]).Append('\n');
        }
        return sb.ToString();
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
                    // Escaped quote inside a quoted field: keep state, consume the pair.
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
