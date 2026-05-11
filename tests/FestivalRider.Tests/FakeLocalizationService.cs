using System.Globalization;
using FestivalRider.Services;

namespace FestivalRider.Tests;

public sealed class FakeLocalizationService : ILocalizationService
{
    private static readonly Dictionary<string, string> Catalog = new()
    {
        ["toast.storage.unreadable"]     = "Saved data was unreadable; reset to a clean state.",
        ["toast.storage.migrationFailed"]= "Migration could not be saved; backed up and reset.",
        ["toast.storage.backupReset"]    = "Saved data uses schema v{0}; backed up and reset to v{1}.",
        ["toast.storage.migrated"]       = "Migrated data v{0} \u2192 v{1}.",
        ["toast.storage.empty"]          = "Saved data was empty; reset to a clean state.",
        ["toast.storage.restored"]       = "Restored {0} band(s) from previous session.",
        ["toast.storage.saveFailed"]     = "Saving failed (storage quota?). Export to CSV, then clear data.",

        ["bundle.error.missingManifest"]          = "Bundle is missing manifest.json.",
        ["bundle.error.invalidManifestJson"]      = "manifest.json is not valid JSON: {0}",
        ["bundle.error.emptyManifest"]            = "manifest.json is empty.",
        ["bundle.error.unknownFormat"]            = "Unrecognized bundle format \"{0}\".",
        ["bundle.error.tooNew"]                   = "Bundle schemaVersion {0} does not match expected {1}.",
        ["bundle.error.tooOld"]                   = "Bundle schemaVersion {0} is too old; regenerate from v{1}.",
        ["bundle.error.noMigrator"]               = "Bundle schemaVersion {0} cannot upgrade to v{1}: no migrator covers v{2}\u2192v{3}.",
        ["bundle.error.manifestParseFailed"]      = "manifest.json is not valid JSON for migration: {0}",
        ["bundle.error.migrationFailed"]          = "Bundle migration failed: {0}",
        ["bundle.error.migratedManifestInvalid"]  = "Migrated manifest is not valid: {0}",
        ["bundle.error.migratedVersionMismatch"]  = "Migrated manifest did not reach current schema version.",
        ["bundle.error.pathTraversal"]            = "Refusing manifest path \"{0}\" (path traversal).",
        ["bundle.error.noShows"]                  = "Bundle manifest has no shows.",
        ["bundle.error.missingShow"]              = "Bundle missing show entry \"{0}\".",
        ["bundle.error.missingBand"]              = "Bundle missing band entry \"{0}\".",
        ["bundle.error.missingRunningOrder"]      = "Bundle missing running order entry \"{0}\".",
        ["bundle.error.notZip"]                   = "Not a valid zip archive: {0}",
        ["bundle.error.importFailed"]             = "Bundle import failed: {0}",

        ["bundle.warning.unlisted"]       = "Ignored unlisted entry \"{0}\".",
        ["bundle.warning.bandSkipped"]    = "Band \"{0}\" ({1}) skipped: incoming UpdatedAt {2} is not newer than local {3}.",
        ["bundle.warning.roNoShow"]       = "Running order {0} (day {1}) skipped: bundle show {2} not found.",
        ["bundle.warning.roNoLocalShow"]  = "Running order {0} (day {1}) skipped: no unambiguous local show matches \"{2}\".",
        ["bundle.warning.roMissingStages"]= "Running order {0} (day {1}) skipped: {2}.",
        ["bundle.warning.roReplaced"]     = "Running order {0} replaced an existing entry with the same id.",

        ["print.day"] = "Day {0}",
        ["print.band.title"] = "{0} \u2014 rider",
        ["print.band.titleWithShow"] = "{0} \u2014 {1} rider",
        ["enum.ContactRole.FOHEngineer"] = "FOH engineer",
    };

    public static readonly FakeLocalizationService Instance = new();

    private readonly Dictionary<string, string> _catalog;
    private readonly CultureInfo _culture;

    public FakeLocalizationService()
    {
        _catalog = Catalog;
        _culture = CultureInfo.InvariantCulture;
    }

    public FakeLocalizationService(CultureInfo culture, Dictionary<string, string> overrideCatalog)
    {
        _culture = culture;
        _overrideCatalog = overrideCatalog;
        _catalog = Catalog;
    }

    private readonly Dictionary<string, string>? _overrideCatalog;

    public string CurrentLocale => _culture.Name.Length > 0 ? _culture.Name : "en";
    public IReadOnlyList<LocaleDescriptor> AvailableLocales => [new LocaleDescriptor(CurrentLocale, CurrentLocale)];
    public CultureInfo Culture => _culture;
#pragma warning disable CS0067
    public event Action? OnLocaleChanged;
#pragma warning restore CS0067

    public Task EnsureLoadedAsync() => Task.CompletedTask;
    public Task SetLocaleAsync(string tag) => Task.CompletedTask;

    public string T(string key)
    {
        if (_overrideCatalog is not null && _overrideCatalog.TryGetValue(key, out var ov)) return ov;
        return _catalog.TryGetValue(key, out var v) ? v : key;
    }

    public string T(string key, params object?[] args)
    {
        var template = T(key);
        if (args.Length == 0) return template;
        try { return string.Format(CultureInfo.InvariantCulture, template, args); }
        catch { return template; }
    }
}
