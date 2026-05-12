using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace FestivalRider.Services;

public class LocalizationService : ILocalizationService
{
    private const string LocaleKey = "festivalrider.locale";

    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private readonly ILogger<LocalizationService> _logger;

    private IReadOnlyList<LocaleDescriptor> _available = Array.Empty<LocaleDescriptor>();
    private Dictionary<string, string> _catalog = new();
    private Dictionary<string, string> _fallback = new();
    private readonly HashSet<string> _loggedMissing = new();
    private bool _loaded;

    private string _currentLocale = "en";
    private CultureInfo _culture = CultureInfo.GetCultureInfo("en");

    public LocalizationService(HttpClient http, IJSRuntime js, ILogger<LocalizationService> logger)
    {
        _http = http;
        _js = js;
        _logger = logger;
    }

    public string CurrentLocale => _currentLocale;
    public IReadOnlyList<LocaleDescriptor> AvailableLocales => _available;
    public CultureInfo Culture => _culture;
    public event Action? OnLocaleChanged;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var descriptors = await _http.GetFromJsonAsync<LocaleDescriptor[]>("i18n/locales.json");
            _available = descriptors ?? Array.Empty<LocaleDescriptor>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load locales.json; falling back to English-only.");
            _available = new[] { new LocaleDescriptor("en", "English") };
        }

        var tag = await ReadPersistedTagAsync();
        if (string.IsNullOrWhiteSpace(tag))
        {
            tag = await DetectBrowserLanguageAsync();
            tag = ResolveTag(tag);
            await PersistTagAsync(tag);
        }
        else
        {
            tag = ResolveTag(tag);
        }

        await LoadCatalogInternalAsync(tag, isFallback: true);
        _currentLocale = tag;
        _culture = SafeGetCulture(tag);

        await SetHtmlLangAsync(tag);
        OnLocaleChanged?.Invoke();
    }

    public async Task SetLocaleAsync(string tag)
    {
        if (string.Equals(tag, _currentLocale, StringComparison.OrdinalIgnoreCase)) return;

        var resolved = ResolveTag(tag);
        await LoadCatalogInternalAsync(resolved, isFallback: false);
        _currentLocale = resolved;
        _culture = SafeGetCulture(resolved);
        _loggedMissing.Clear();

        await PersistTagAsync(resolved);
        await SetHtmlLangAsync(resolved);
        OnLocaleChanged?.Invoke();
    }

    public string T(string key)
    {
        if (_catalog.TryGetValue(key, out var val)) return val;
        if (_fallback.TryGetValue(key, out var fb))
        {
#if DEBUG
            if (_loggedMissing.Add(key))
                _logger.LogWarning("Missing key \"{Key}\" in locale \"{Locale}\"; falling back to English.", key, _currentLocale);
#endif
            return fb;
        }
        return key;
    }

    public string T(string key, params object?[] args)
    {
        var template = T(key);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch
        {
            return template;
        }
    }

    private async Task LoadCatalogInternalAsync(string tag, bool isFallback)
    {
        var dict = await FetchCatalogAsync(tag);

        if (string.Equals(tag, "en", StringComparison.OrdinalIgnoreCase))
            _fallback = dict;
        else if (isFallback || _fallback.Count == 0)
            _fallback = await FetchCatalogAsync("en");

        _catalog = dict;
    }

    private async Task<Dictionary<string, string>> FetchCatalogAsync(string tag)
    {
        var file = tag.ToLowerInvariant() + ".json";
        try
        {
            var result = await _http.GetFromJsonAsync<Dictionary<string, string>>($"i18n/{file}");
            return result ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch catalog for locale \"{Tag}\".", tag);
            return new Dictionary<string, string>();
        }
    }

    private string ResolveTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return "en";

        var lower = tag.ToLowerInvariant();

        // exact match (case-insensitive on tag)
        if (_available.Any(d => d.Tag.ToLowerInvariant() == lower))
            return _available.First(d => d.Tag.ToLowerInvariant() == lower).Tag;

        // language-only fallback (e.g. "en-US" -> "en")
        var lang = lower.Split('-')[0];
        if (_available.Any(d => d.Tag.ToLowerInvariant() == lang))
            return _available.First(d => d.Tag.ToLowerInvariant() == lang).Tag;

        return "en";
    }

    private async Task<string?> ReadPersistedTagAsync()
    {
        try
        {
            return await _js.InvokeAsync<string?>("festivalRiderStorage.getItem", LocaleKey);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read persisted locale.");
            return null;
        }
    }

    private async Task PersistTagAsync(string tag)
    {
        try
        {
            await _js.InvokeAsync<bool>("festivalRiderStorage.setItem", LocaleKey, tag);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not persist locale.");
        }
    }

    private async Task<string> DetectBrowserLanguageAsync()
    {
        try
        {
            return await _js.InvokeAsync<string>("festivalRiderI18n.getNavigatorLanguage") ?? "en";
        }
        catch
        {
            return "en";
        }
    }

    private async Task SetHtmlLangAsync(string tag)
    {
        try
        {
            await _js.InvokeVoidAsync("festivalRiderI18n.setHtmlLang", tag);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not set html lang attribute.");
        }
    }

    private static CultureInfo SafeGetCulture(string tag)
    {
        try { return CultureInfo.GetCultureInfo(tag); }
        catch { return CultureInfo.GetCultureInfo("en"); }
    }
}
