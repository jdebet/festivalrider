using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FestivalRider.Models;
using Microsoft.Extensions.Logging;

namespace FestivalRider.Services;

public class BundleService : IBundleService
{
    public const string Format = "festivalrider-bundle";
    public const string ManifestEntry = "manifest.json";
    public const string ShowEntry = "show.csv";
    public const string BandsPrefix = "bands/";
    public const string RunningOrdersPrefix = "running-orders/";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly IExportService _export;
    private readonly ILogger<BundleService> _logger;

    public BundleService(IExportService export, ILogger<BundleService> logger)
    {
        _export = export;
        _logger = logger;
    }

    private sealed class Manifest
    {
        public string Format { get; set; } = BundleService.Format;
        public int SchemaVersion { get; set; }
        public DateTimeOffset ExportedAt { get; set; }
        public string Show { get; set; } = ShowEntry;
        public List<string> Bands { get; set; } = new();
        public List<string> RunningOrders { get; set; } = new();
    }

    public byte[] ExportBundle(AppState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        var bandsSorted = state.Bands.OrderBy(b => b.Id).ToList();
        var ordersSorted = state.RunningOrders.OrderBy(o => o.Id).ToList();

        var manifest = new Manifest
        {
            Format = Format,
            SchemaVersion = state.SchemaVersion,
            ExportedAt = DateTimeOffset.UtcNow,
            Show = ShowEntry,
            Bands = bandsSorted.Select(b => $"{BandsPrefix}{b.Id}.csv").ToList(),
            RunningOrders = ordersSorted.Select(o => $"{RunningOrdersPrefix}{o.Id}.csv").ToList(),
        };

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, ShowEntry, _export.ExportShowCsv(state.ShowData));
            foreach (var band in bandsSorted)
                WriteEntry(zip, $"{BandsPrefix}{band.Id}.csv", _export.ExportBandCsv(band));
            foreach (var order in ordersSorted)
                WriteEntry(zip, $"{RunningOrdersPrefix}{order.Id}.csv", _export.ExportRunningOrderCsv(order));

            // manifest written last to ensure all entries exist
            var manifestJson = JsonSerializer.Serialize(manifest, JsonOpts);
            WriteEntry(zip, ManifestEntry, manifestJson);
        }
        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        var bytes = Utf8NoBom.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
        s.Flush();
    }

    public BundleImportResult ImportBundle(Stream zipStream)
    {
        if (zipStream is null) throw new ArgumentNullException(nameof(zipStream));
        var warnings = new List<string>();
        try
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            var manifestEntry = archive.GetEntry(ManifestEntry);
            if (manifestEntry is null)
                return Fail("Bundle is missing manifest.json.");

            Manifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<Manifest>(ReadAll(manifestEntry), JsonOpts);
            }
            catch (JsonException ex)
            {
                return Fail($"manifest.json is not valid JSON: {ex.Message}");
            }
            if (manifest is null)
                return Fail("manifest.json is empty.");
            if (!string.Equals(manifest.Format, Format, StringComparison.Ordinal))
                return Fail($"Unrecognized bundle format \"{manifest.Format}\".");

            var current = new AppState();
            if (manifest.SchemaVersion != current.SchemaVersion)
            {
                _logger.LogWarning(
                    "Bundle schemaVersion {Found} does not match {Expected}; refusing import.",
                    manifest.SchemaVersion, current.SchemaVersion);
                return Fail($"Bundle schemaVersion {manifest.SchemaVersion} does not match expected {current.SchemaVersion}.");
            }

            foreach (var path in EnumerateListedPaths(manifest))
            {
                if (path.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(path))
                    return Fail($"Refusing manifest path \"{path}\" (path traversal).");
            }

            var listed = new HashSet<string>(EnumerateListedPaths(manifest), StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName == ManifestEntry) continue;
                if (string.IsNullOrEmpty(entry.Name)) continue; // skip directory entries
                if (!listed.Contains(entry.FullName))
                    warnings.Add($"Ignored unlisted entry \"{entry.FullName}\".");
            }

            // Show
            var showEntry = archive.GetEntry(manifest.Show);
            if (showEntry is null) return Fail($"Bundle missing show entry \"{manifest.Show}\".");
            var show = _export.ImportShowCsv(ReadAll(showEntry));

            // Bands
            var bands = new List<Band>();
            foreach (var bandPath in manifest.Bands)
            {
                var entry = archive.GetEntry(bandPath);
                if (entry is null) return Fail($"Bundle missing band entry \"{bandPath}\".");
                bands.Add(_export.ImportBandCsv(ReadAll(entry)));
            }

            // Running orders
            var orders = new List<RunningOrder>();
            foreach (var orderPath in manifest.RunningOrders)
            {
                var entry = archive.GetEntry(orderPath);
                if (entry is null) return Fail($"Bundle missing running order entry \"{orderPath}\".");

                var order = _export.ImportRunningOrderCsv(ReadAll(entry), show, bands);
                order.Id = ParseIdFromPath(orderPath, RunningOrdersPrefix) ?? order.Id;
                orders.Add(order);
            }

            var state = new AppState
            {
                SchemaVersion = manifest.SchemaVersion,
                ShowData = show,
                Bands = bands,
                RunningOrders = orders,
            };

            return new BundleImportResult(state, bands.Count, orders.Count, warnings, null);
        }
        catch (InvalidDataException ex)
        {
            return Fail($"Not a valid zip archive: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bundle import failed.");
            return Fail($"Bundle import failed: {ex.Message}");
        }

        BundleImportResult Fail(string error) =>
            new(null, 0, 0, warnings, error);
    }

    private static IEnumerable<string> EnumerateListedPaths(Manifest m)
    {
        yield return m.Show;
        foreach (var b in m.Bands) yield return b;
        foreach (var r in m.RunningOrders) yield return r;
    }

    private static string ReadAll(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var reader = new StreamReader(s, Utf8NoBom);
        return reader.ReadToEnd();
    }

    private static Guid? ParseIdFromPath(string path, string prefix)
    {
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var name = Path.GetFileNameWithoutExtension(path[prefix.Length..]);
        return Guid.TryParse(name, out var g) ? g : null;
    }
}
