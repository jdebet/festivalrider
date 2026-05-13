using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FestivalRider.BundleMigrators;
using FestivalRider.Models;
using Microsoft.Extensions.Logging;

namespace FestivalRider.Services;

public class BundleService : IBundleService
{
    public const string Format = "festivalrider-bundle";
    public const string MasterFormat = "festivalrider-master-bundle";
    public const string ManifestEntry = "manifest.json";
    public const string ShowsPrefix = "shows/";
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
    private readonly ILocalizationService _loc;
    private readonly IReadOnlyDictionary<int, IBundleMigrator> _migrators;

    public BundleService(
        IExportService export,
        ILogger<BundleService> logger,
        ILocalizationService loc,
        IEnumerable<IBundleMigrator>? migrators = null)
    {
        _export = export;
        _logger = logger;
        _loc = loc;
        _migrators = BuildMigratorIndex(migrators);
    }

    private static IReadOnlyDictionary<int, IBundleMigrator> BuildMigratorIndex(IEnumerable<IBundleMigrator>? migrators)
    {
        var dict = new Dictionary<int, IBundleMigrator>();
        if (migrators is null) return dict;
        foreach (var m in migrators)
        {
            if (m.ToVersion != m.FromVersion + 1)
                throw new InvalidOperationException(
                    $"Bundle migrator {m.GetType().Name} is not step-wise: FromVersion={m.FromVersion}, ToVersion={m.ToVersion}.");
            if (!dict.TryAdd(m.FromVersion, m))
                throw new InvalidOperationException(
                    $"Duplicate IBundleMigrator registered for FromVersion={m.FromVersion}: {dict[m.FromVersion].GetType().Name} and {m.GetType().Name}.");
        }
        return dict;
    }

    // v5 single-show manifest
    private sealed class Manifest
    {
        public string Format { get; set; } = BundleService.Format;
        public int SchemaVersion { get; set; }
        public DateTimeOffset ExportedAt { get; set; }
        public string Show { get; set; } = string.Empty;
        public List<string> Bands { get; set; } = new();
        public List<string> RunningOrders { get; set; } = new();
    }

    // v5 master manifest
    private sealed class MasterManifest
    {
        public string Format { get; set; } = BundleService.MasterFormat;
        public int SchemaVersion { get; set; }
        public DateTimeOffset ExportedAt { get; set; }
        public List<string> Shows { get; set; } = new();
    }

    public byte[] ExportBundle(ShowData show)
    {
        if (show is null) throw new ArgumentNullException(nameof(show));

        var bandsSorted = show.Bands.OrderBy(b => b.Id).ToList();
        var ordersSorted = show.RunningOrders.OrderBy(o => o.Id).ToList();

        var manifest = new Manifest
        {
            Format = Format,
            SchemaVersion = 5,
            ExportedAt = DateTimeOffset.UtcNow,
            Show = $"{ShowsPrefix}{show.Id}.csv",
            Bands = bandsSorted.Select(b => $"{BandsPrefix}{b.Id}.csv").ToList(),
            RunningOrders = ordersSorted.Select(o => $"{RunningOrdersPrefix}{o.Id}.csv").ToList(),
        };

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, $"{ShowsPrefix}{show.Id}.csv", _export.ExportShowCsv(show));
            foreach (var band in bandsSorted)
                WriteEntry(zip, $"{BandsPrefix}{band.Id}.csv", _export.ExportBandCsv(band));
            foreach (var order in ordersSorted)
                WriteEntry(zip, $"{RunningOrdersPrefix}{order.Id}.csv", _export.ExportRunningOrderCsv(order));

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOpts);
            WriteEntry(zip, ManifestEntry, manifestJson);
        }
        return ms.ToArray();
    }

    public byte[] ExportMasterBundle(AppState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        var showsSorted = state.Shows.OrderBy(s => s.Id).ToList();
        var nestedZips = new Dictionary<string, byte[]>();
        foreach (var show in showsSorted)
        {
            nestedZips[$"{ShowsPrefix}{show.Id}.zip"] = ExportBundle(show);
        }

        var manifest = new MasterManifest
        {
            Format = MasterFormat,
            SchemaVersion = 5,
            ExportedAt = DateTimeOffset.UtcNow,
            Shows = nestedZips.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
        };

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var kv in nestedZips.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var entry = zip.CreateEntry(kv.Key, CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(kv.Value, 0, kv.Value.Length);
            }
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

    public BundleImportResult ImportBundle(Stream zipStream, Guid targetShowId, BundleImportMode mode = BundleImportMode.Replace, AppState? currentState = null)
    {
        if (zipStream is null) throw new ArgumentNullException(nameof(zipStream));
        if (mode == BundleImportMode.Merge && currentState is null)
            throw new ArgumentNullException(nameof(currentState), "currentState is required for Merge mode.");
        var warnings = new List<string>();
        try
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            IDictionary<string, string> entryTexts = ReadAllEntryTexts(archive);
            if (!entryTexts.TryGetValue(ManifestEntry, out var manifestJson))
                return Fail(_loc.T("bundle.error.missingManifest"));

            Manifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<Manifest>(manifestJson, JsonOpts);
            }
            catch (JsonException ex)
            {
                return Fail(_loc.T("bundle.error.invalidManifestJson", ex.Message));
            }
            if (manifest is null)
                return Fail(_loc.T("bundle.error.emptyManifest"));
            if (!string.Equals(manifest.Format, Format, StringComparison.Ordinal))
                return Fail(_loc.T("bundle.error.unknownFormat", manifest.Format));

            var current = new AppState();

            if (manifest.SchemaVersion != current.SchemaVersion)
            {
                if (_migrators.Count == 0)
                {
                    _logger.LogWarning(
                        "Bundle schemaVersion {Found} does not match {Expected}; no migrators registered, refusing import.",
                        manifest.SchemaVersion, current.SchemaVersion);
                    return Fail(manifest.SchemaVersion < current.SchemaVersion
                        ? _loc.T("bundle.error.tooOld", manifest.SchemaVersion, current.SchemaVersion)
                        : _loc.T("bundle.error.tooNew", manifest.SchemaVersion, current.SchemaVersion));
                }
                if (manifest.SchemaVersion > current.SchemaVersion)
                {
                    _logger.LogWarning(
                        "Bundle schemaVersion {Found} is newer than {Expected}; downgrade not supported.",
                        manifest.SchemaVersion, current.SchemaVersion);
                    return Fail(_loc.T("bundle.error.tooNew", manifest.SchemaVersion, current.SchemaVersion));
                }

                for (int v = manifest.SchemaVersion; v < current.SchemaVersion; v++)
                {
                    if (!_migrators.ContainsKey(v))
                    {
                        _logger.LogWarning(
                            "No bundle migrator covers v{From} -> v{To}; refusing import.", v, v + 1);
                        return Fail(_loc.T("bundle.error.noMigrator", manifest.SchemaVersion, current.SchemaVersion, v, v + 1));
                    }
                }

                IDictionary<string, object?> manifestDict;
                try
                {
                    manifestDict = BundleScratch.ParseManifest(manifestJson);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse manifest.json into property bag for migration.");
                    return Fail(_loc.T("bundle.error.manifestParseFailed", ex.Message));
                }

                var scratchEntries = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in entryTexts)
                {
                    if (kv.Key == ManifestEntry) continue;
                    scratchEntries[kv.Key] = kv.Value;
                }
                var scratch = new BundleScratch(manifestDict, scratchEntries, manifest.SchemaVersion);

                try
                {
                    for (int v = scratch.SchemaVersion; v < current.SchemaVersion; v++)
                    {
                        var mig = _migrators[v];
                        mig.Migrate(scratch, warnings);
                        scratch.Manifest["schemaVersion"] = mig.ToVersion;
                        scratch.SchemaVersion = mig.ToVersion;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Bundle migrator threw; refusing import.");
                    return Fail(_loc.T("bundle.error.migrationFailed", ex.Message));
                }

                string migratedJson;
                try
                {
                    migratedJson = scratch.SerializeManifest();
                    manifest = JsonSerializer.Deserialize<Manifest>(migratedJson, JsonOpts);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Migrated manifest failed to round-trip.");
                    return Fail(_loc.T("bundle.error.migratedManifestInvalid", ex.Message));
                }
                if (manifest is null || manifest.SchemaVersion != current.SchemaVersion)
                    return Fail(_loc.T("bundle.error.migratedVersionMismatch"));

                entryTexts = scratch.Entries;
            }
            else
            {
                entryTexts.Remove(ManifestEntry);
            }

            foreach (var path in EnumerateListedPaths(manifest))
            {
                if (path.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(path))
                    return Fail(_loc.T("bundle.error.pathTraversal", path));
            }

            var listed = new HashSet<string>(EnumerateListedPaths(manifest), StringComparer.Ordinal);
            foreach (var name in entryTexts.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (!listed.Contains(name))
                    warnings.Add(_loc.T("bundle.warning.unlisted", name));
            }

            // Show
            if (string.IsNullOrEmpty(manifest.Show))
                return Fail(_loc.T("bundle.error.noShows"));
            if (!entryTexts.TryGetValue(manifest.Show, out var showCsv))
                return Fail(_loc.T("bundle.error.missingShow", manifest.Show));
            var show = _export.ImportShowCsv(showCsv);
            var pathId = ParseIdFromPath(manifest.Show, ShowsPrefix);
            if (pathId is { } g) show.Id = g;

            // Bands
            var bands = new List<Band>();
            foreach (var bandPath in manifest.Bands)
            {
                if (!entryTexts.TryGetValue(bandPath, out var bandCsv))
                    return Fail(_loc.T("bundle.error.missingBand", bandPath));
                bands.Add(_export.ImportBandCsv(bandCsv));
            }

            // Running orders
            var orders = new List<RunningOrder>();
            foreach (var orderPath in manifest.RunningOrders)
            {
                if (!entryTexts.TryGetValue(orderPath, out var csv))
                    return Fail(_loc.T("bundle.error.missingRunningOrder", orderPath));
                var order = _export.ImportRunningOrderCsv(csv, show, bands);
                order.Id = ParseIdFromPath(orderPath, RunningOrdersPrefix) ?? order.Id;
                if (order.ShowId == Guid.Empty) order.ShowId = show.Id;
                orders.Add(order);
            }

            if (mode == BundleImportMode.Replace)
            {
                var state = currentState ?? new AppState();
                if (currentState is null)
                    state.Shows.Clear();
                var targetShow = state.Shows.FirstOrDefault(s => s.Id == targetShowId);
                if (targetShow is null)
                {
                    targetShow = new ShowData { Id = targetShowId };
                    state.Shows.Add(targetShow);
                }
                state.ActiveShowId = targetShowId;
                targetShow.Name = show.Name;
                targetShow.Address = show.Address;
                targetShow.DateOfOpening = show.DateOfOpening;
                targetShow.ShowDayCount = show.ShowDayCount;
                targetShow.Stages = show.Stages;
                targetShow.Bands = bands;
                foreach (var o in orders) o.ShowId = targetShowId;
                targetShow.RunningOrders = orders;
                return new BundleImportResult(state, bands.Count, orders.Count, warnings, null);
            }

            // Merge
            var (merged, stats) = MergeInto(currentState!, targetShowId, show, bands, orders, warnings, _loc);
            return new BundleImportResult(
                merged,
                stats.BandsAdded + stats.BandsUpdated,
                stats.RunningOrdersAdded + stats.RunningOrdersUpdated,
                warnings,
                null,
                stats);
        }
        catch (InvalidDataException ex)
        {
            return Fail(_loc.T("bundle.error.notZip", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bundle import failed.");
            return Fail(_loc.T("bundle.error.importFailed", ex.Message));
        }

        BundleImportResult Fail(string error) =>
            new(null, 0, 0, warnings, error);
    }

    public BundleImportResult ImportMasterBundle(Stream zipStream, BundleImportMode mode = BundleImportMode.Replace, AppState? currentState = null)
    {
        if (zipStream is null) throw new ArgumentNullException(nameof(zipStream));
        if (mode == BundleImportMode.Merge && currentState is null)
            throw new ArgumentNullException(nameof(currentState), "currentState is required for Merge mode.");
        var warnings = new List<string>();
        try
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            var manifestEntry = archive.GetEntry(ManifestEntry)
                ?? throw new InvalidDataException("Missing manifest.json");
            string manifestJson;
            using (var r = new StreamReader(manifestEntry.Open(), Utf8NoBom))
            {
                manifestJson = r.ReadToEnd();
            }

            MasterManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<MasterManifest>(manifestJson, JsonOpts);
            }
            catch (JsonException ex)
            {
                return Fail(_loc.T("bundle.error.invalidManifestJson", ex.Message));
            }
            if (manifest is null)
                return Fail(_loc.T("bundle.error.emptyManifest"));
            if (!string.Equals(manifest.Format, MasterFormat, StringComparison.Ordinal))
                return Fail(_loc.T("bundle.error.unknownFormat", manifest.Format));

            var state = currentState ?? new AppState();
            if (currentState is null)
                state.Shows.Clear();
            int totalBands = 0, totalOrders = 0;

            foreach (var showPath in manifest.Shows.OrderBy(p => p, StringComparer.Ordinal))
            {
                var nestedEntry = archive.GetEntry(showPath);
                if (nestedEntry is null)
                {
                    warnings.Add(_loc.T("bundle.warning.missingNested", showPath));
                    continue;
                }
                byte[] nestedZipBytes;
                using (var s = nestedEntry.Open())
                {
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    nestedZipBytes = ms.ToArray();
                }
                using var nestedStream = new MemoryStream(nestedZipBytes);
                var targetShowId = ParseIdFromPath(showPath, ShowsPrefix) ?? state.ActiveShowId;
                var nestedResult = ImportBundle(nestedStream, targetShowId, mode, state);
                if (nestedResult.Error is { } err)
                {
                    warnings.Add(_loc.T("bundle.warning.nestedImportFailed", showPath, err));
                    continue;
                }
                if (nestedResult.State is { } ns)
                {
                    state = ns;
                    totalBands += nestedResult.BandCount;
                    totalOrders += nestedResult.RunningOrderCount;
                }
            }

            return new BundleImportResult(state, totalBands, totalOrders, warnings, null);
        }
        catch (InvalidDataException ex)
        {
            return Fail(_loc.T("bundle.error.notZip", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Master bundle import failed.");
            return Fail(_loc.T("bundle.error.importFailed", ex.Message));
        }

        BundleImportResult Fail(string error) =>
            new(null, 0, 0, warnings, error);
    }

    private static IEnumerable<string> EnumerateListedPaths(Manifest m)
    {
        if (!string.IsNullOrEmpty(m.Show)) yield return m.Show;
        foreach (var b in m.Bands) yield return b;
        foreach (var r in m.RunningOrders) yield return r;
    }

    private static string ReadAll(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var reader = new StreamReader(s, Utf8NoBom);
        return reader.ReadToEnd();
    }

    private static Dictionary<string, string> ReadAllEntryTexts(ZipArchive archive)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            dict[entry.FullName] = ReadAll(entry);
        }
        return dict;
    }

    private static Guid? ParseIdFromPath(string path, string prefix)
    {
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var name = Path.GetFileNameWithoutExtension(path[prefix.Length..]);
        return Guid.TryParse(name, out var g) ? g : null;
    }

    private static (AppState State, MergeStats Stats) MergeInto(
        AppState currentState,
        Guid targetShowId,
        ShowData incomingShow,
        IReadOnlyList<Band> incomingBands,
        IReadOnlyList<RunningOrder> incomingOrders,
        List<string> warnings,
        ILocalizationService loc)
    {
        var targetShow = currentState.Shows.FirstOrDefault(s => s.Id == targetShowId);
        if (targetShow is null)
        {
            targetShow = new ShowData { Id = targetShowId };
            currentState.Shows.Add(targetShow);
        }

        // Preserve target show metadata and stages; only merge bands and running orders.
        var bandsById = targetShow.Bands.ToDictionary(b => b.Id);
        int bAdded = 0, bUpdated = 0, bSkipped = 0;
        foreach (var inc in incomingBands)
        {
            if (bandsById.TryGetValue(inc.Id, out var existing))
            {
                if (inc.UpdatedAt > existing.UpdatedAt)
                {
                    bandsById[inc.Id] = inc;
                    bUpdated++;
                }
                else
                {
                    bSkipped++;
                    warnings.Add(loc.T("bundle.warning.bandSkipped", inc.Name, inc.Id, inc.UpdatedAt.ToString("o"), existing.UpdatedAt.ToString("o")));
                }
            }
            else
            {
                bandsById[inc.Id] = inc;
                bAdded++;
            }
        }

        // Stage remap: incoming stages matched by name to target show stages.
        var localStageByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ambiguousLocalStageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stage in targetShow.Stages)
        {
            var key = NameKey(stage.Name);
            if (key.Length == 0) continue;
            if (!localStageByName.TryAdd(key, stage.Id))
                ambiguousLocalStageNames.Add(key);
        }
        var senderStageNameById = incomingShow.Stages.ToDictionary(s => s.Id, s => s.Name ?? string.Empty);

        var ordersById = targetShow.RunningOrders.ToDictionary(o => o.Id);
        int oAdded = 0, oUpdated = 0, oSkipped = 0;

        foreach (var inc in incomingOrders)
        {
            var remapped = new List<RunningOrderSlot>(inc.Slots.Count);
            var missing = new List<string>();
            var ambiguous = new List<string>();
            foreach (var slot in inc.Slots)
            {
                var senderName = senderStageNameById.TryGetValue(slot.StageId, out var n) ? n : string.Empty;
                var key = NameKey(senderName);
                if (key.Length == 0)
                {
                    missing.Add($"#{slot.StageId}");
                    continue;
                }
                if (ambiguousLocalStageNames.Contains(key))
                {
                    ambiguous.Add(senderName);
                    continue;
                }
                if (!localStageByName.TryGetValue(key, out var localId))
                {
                    missing.Add(senderName);
                    continue;
                }
                remapped.Add(slot with { StageId = localId });
            }

            if (missing.Count > 0 || ambiguous.Count > 0)
            {
                oSkipped++;
                var parts = new List<string>();
                if (missing.Count > 0) parts.Add($"missing local stage(s): {string.Join(", ", missing.Distinct())}");
                if (ambiguous.Count > 0) parts.Add($"ambiguous local stage name(s): {string.Join(", ", ambiguous.Distinct())}");
                warnings.Add(loc.T("bundle.warning.roMissingStages", inc.Id, inc.ShowDayNumber, string.Join("; ", parts)));
                continue;
            }

            var ro = new RunningOrder
            {
                Id = inc.Id,
                ShowId = targetShowId,
                ShowDayNumber = inc.ShowDayNumber,
                Slots = remapped,
            };
            if (ordersById.ContainsKey(inc.Id))
            {
                warnings.Add(loc.T("bundle.warning.roReplaced", inc.Id));
                ordersById[inc.Id] = ro;
                oUpdated++;
            }
            else
            {
                ordersById[inc.Id] = ro;
                oAdded++;
            }
        }

        targetShow.Bands = bandsById.Values.OrderBy(b => b.Id).ToList();
        targetShow.RunningOrders = ordersById.Values.OrderBy(o => o.Id).ToList();

        return (currentState, new MergeStats(bAdded, bUpdated, bSkipped, oAdded, oUpdated, oSkipped));
    }

    private static string NameKey(string? name) => (name ?? string.Empty).Trim();
}
