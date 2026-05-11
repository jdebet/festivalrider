using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FestivalRider.Services;
using Xunit;

namespace FestivalRider.Tests;

public sealed class LocalizationCatalogTests
{
    private static readonly string I18nDir =
        Path.Combine(AppContext.BaseDirectory, "i18n");

    private static Dictionary<string, string> LoadCatalog(string filename)
    {
        var path = Path.Combine(I18nDir, filename);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? throw new InvalidOperationException($"Catalog {filename} deserialized to null.");
    }

    private static IEnumerable<string> CatalogFilenames()
        => Directory.GetFiles(I18nDir, "*.json")
                    .Select(Path.GetFileName)
                    .Where(f => f != "locales.json")
                    .OrderBy(f => f)!;

    private static IEnumerable<string> NonEnglishCatalogFilenames()
        => CatalogFilenames().Where(f => f != "en.json");

    private static int CountPlaceholders(string value)
    {
        var matches = Regex.Matches(value, @"\{(\d+)\}");
        if (matches.Count == 0) return 0;
        return matches.Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                      .Distinct()
                      .Count();
    }

    [Fact]
    public void AllCatalogs_AreValidJsonDictionaries()
    {
        foreach (var file in CatalogFilenames())
        {
            var path = Path.Combine(I18nDir, file);
            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            Assert.True(dict is not null, $"{file} deserialised to null.");
            Assert.True(dict!.Count > 0, $"{file} is empty.");
        }
    }

    [Fact]
    public void NonEnglishCatalogs_HaveExactSameKeySetAsEnglish()
    {
        var enKeys = LoadCatalog("en.json").Keys.OrderBy(k => k).ToList();
        foreach (var file in NonEnglishCatalogFilenames())
        {
            var keys = LoadCatalog(file).Keys.OrderBy(k => k).ToList();
            var missing = enKeys.Except(keys).ToList();
            var extra = keys.Except(enKeys).ToList();
            Assert.True(missing.Count == 0,
                $"{file} is missing keys: {string.Join(", ", missing)}");
            Assert.True(extra.Count == 0,
                $"{file} has extra keys not in en.json: {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void AllCatalogs_PlaceholderCountsMatchEnglish()
    {
        var en = LoadCatalog("en.json");
        foreach (var file in NonEnglishCatalogFilenames())
        {
            var catalog = LoadCatalog(file);
            foreach (var (key, enVal) in en)
            {
                if (!catalog.TryGetValue(key, out var locVal)) continue;
                var enCount = CountPlaceholders(enVal);
                var locCount = CountPlaceholders(locVal);
                Assert.True(enCount == locCount,
                    $"Key \"{key}\" in {file}: en has {enCount} placeholder(s) but {file} has {locCount}.");
            }
        }
    }

    [Fact]
    public void EnglishCatalog_CoversAllModelEnumValues()
    {
        var en = LoadCatalog("en.json");
        var modelAssembly = typeof(FestivalRider.Models.Band).Assembly;
        var enumTypes = modelAssembly.GetTypes()
            .Where(t => t.IsEnum && t.Namespace == "FestivalRider.Models");

        var missing = new List<string>();
        foreach (var type in enumTypes)
        {
            foreach (var name in Enum.GetNames(type))
            {
                var key = $"enum.{type.Name}.{name}";
                if (!en.ContainsKey(key))
                    missing.Add(key);
            }
        }

        Assert.True(missing.Count == 0,
            $"en.json is missing enum keys: {string.Join(", ", missing)}");
    }

    [Fact]
    public void LocalesJson_MatchesCatalogFiles()
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var localesPath = Path.Combine(I18nDir, "locales.json");
        var locales = JsonSerializer.Deserialize<LocaleDescriptor[]>(
            File.ReadAllText(localesPath), opts);
        Assert.NotNull(locales);
        Assert.True(locales!.Length > 0, "locales.json is empty.");

        foreach (var loc in locales)
        {
            var filename = loc.Tag.ToLowerInvariant() + ".json";
            Assert.True(File.Exists(Path.Combine(I18nDir, filename)),
                $"locales.json entry \"{loc.Tag}\" has no matching catalog file {filename}.");
        }

        var catalogTags = CatalogFilenames()
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localeTags = locales
            .Select(l => l.Tag.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unregistered = catalogTags.Except(localeTags).ToList();
        Assert.True(unregistered.Count == 0,
            $"Catalog file(s) have no locales.json entry: {string.Join(", ", unregistered)}");
    }

    [Fact]
    public void LocalizationKeys_ParityWithEnglishCatalog()
    {
        var en = LoadCatalog("en.json");
        var constants = CollectConstantValues(typeof(LocalizationKeys));

        var missingInJson = constants.Except(en.Keys).ToList();
        var missingInConstants = en.Keys.Except(constants).ToList();

        Assert.True(missingInJson.Count == 0,
            $"LocalizationKeys has constants not in en.json: {string.Join(", ", missingInJson)}");
        Assert.True(missingInConstants.Count == 0,
            $"en.json has keys missing from LocalizationKeys: {string.Join(", ", missingInConstants)}");
    }

    private static HashSet<string> CollectConstantValues(Type type)
    {
        var result = new HashSet<string>();
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)))
        {
            result.Add((string)field.GetValue(null)!);
        }
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public)
            .Where(t => t.IsClass))
        {
            result.UnionWith(CollectConstantValues(nested));
        }
        return result;
    }
}
