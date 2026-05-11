using System.Globalization;

namespace FestivalRider.Services;

public interface ILocalizationService
{
    string CurrentLocale { get; }
    IReadOnlyList<LocaleDescriptor> AvailableLocales { get; }
    CultureInfo Culture { get; }
    event Action? OnLocaleChanged;
    Task EnsureLoadedAsync();
    Task SetLocaleAsync(string tag);
    string T(string key);
    string T(string key, params object?[] args);
}
