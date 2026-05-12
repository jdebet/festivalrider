namespace FestivalRider.BundleMigrators;

// Plan 017. Rewrites band CSV rows:
//   - Tech.Foh,StageToFohRoundTrip -> StageToFohRoundTripCount (True/False -> 1/0)
//   - adds Tech.Cable,MaxLengthMeters empty rows
//   - adds Tech.LightingMachine,Location empty rows
//   - adds Tech.Foh,OutputProtocolOther / OutputLocationOther empty rows
//
// FROZEN ON SHIP. Bug fixes land as a successor migrator.
public sealed class V3ToV4BundleMigrator : IBundleMigrator
{
    public int FromVersion => 3;
    public int ToVersion => 4;

    public void Migrate(BundleScratch scratch, IList<string> warnings)
    {
        if (scratch is null) throw new ArgumentNullException(nameof(scratch));
        if (warnings is null) throw new ArgumentNullException(nameof(warnings));

        var bandPaths = scratch.Entries.Keys
            .Where(k => k.StartsWith("bands/", StringComparison.Ordinal))
            .ToList();

        foreach (var path in bandPaths)
        {
            scratch.Entries[path] = MigrateBandCsv(scratch.Entries[path]);
        }
    }

    internal static string MigrateBandCsv(string csv)
    {
        if (string.IsNullOrEmpty(csv)) return csv;
        var rows = SplitCsvRows(csv).ToList();
        if (rows.Count == 0) return csv;

        var header = rows[0];
        var sb = new System.Text.StringBuilder();
        sb.Append(header).Append('\n');

        int maxCableIndex = -1;
        int maxMachineIndex = -1;
        bool hasOutputProtocolOther = false;
        bool hasOutputLocationOther = false;

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Length == 0)
            {
                sb.Append('\n');
                continue;
            }

            // Rename StageToFohRoundTrip -> StageToFohRoundTripCount and convert bool->int
            if (row.StartsWith("Tech.Foh,StageToFohRoundTrip,", StringComparison.Ordinal))
            {
                var remainder = row["Tech.Foh,StageToFohRoundTrip,".Length..];
                var firstComma = remainder.IndexOf(',');
                var rawValue = firstComma >= 0 ? remainder[..firstComma] : remainder;
                var intVal = bool.TryParse(rawValue, out var b) && b ? "1" : "0";
                sb.Append("Tech.Foh,StageToFohRoundTripCount,").Append(intVal);
                if (firstComma >= 0)
                    sb.Append(remainder[firstComma..]);
                sb.Append('\n');
                continue;
            }

            // Track highest indices per section
            if (row.StartsWith("Tech.Cable,", StringComparison.Ordinal))
            {
                var idx = ExtractIndex(row);
                if (int.TryParse(idx, out var n)) maxCableIndex = Math.Max(maxCableIndex, n);
            }
            else if (row.StartsWith("Tech.LightingMachine,", StringComparison.Ordinal))
            {
                var idx = ExtractIndex(row);
                if (int.TryParse(idx, out var n)) maxMachineIndex = Math.Max(maxMachineIndex, n);
            }
            else if (row.StartsWith("Tech.Foh,OutputProtocolOther,", StringComparison.Ordinal))
            {
                hasOutputProtocolOther = true;
            }
            else if (row.StartsWith("Tech.Foh,OutputLocationOther,", StringComparison.Ordinal))
            {
                hasOutputLocationOther = true;
            }

            sb.Append(row).Append('\n');
        }

        // Append missing rows at the end
        for (int idx = 0; idx <= maxCableIndex; idx++)
            sb.Append($"Tech.Cable,MaxLengthMeters,,{idx},\n");

        for (int idx = 0; idx <= maxMachineIndex; idx++)
            sb.Append($"Tech.LightingMachine,Location,,{idx},\n");

        if (!hasOutputProtocolOther)
            sb.Append("Tech.Foh,OutputProtocolOther,,,\n");
        if (!hasOutputLocationOther)
            sb.Append("Tech.Foh,OutputLocationOther,,,\n");

        return sb.ToString();
    }

    private static string ExtractIndex(string row)
    {
        // Row format: Section,Key,Value,Index,Notes
        var parts = row.Split(',');
        return parts.Length >= 4 ? parts[3] : string.Empty;
    }

    private static IEnumerable<string> SplitCsvRows(string csv)
    {
        var sb = new System.Text.StringBuilder();
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
