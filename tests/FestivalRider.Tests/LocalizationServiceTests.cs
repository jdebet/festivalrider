using System.Net;
using System.Text;
using FestivalRider.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FestivalRider.Tests;

public sealed class LocalizationServiceTests
{
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string url, string json) => _responses[url] = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery?.TrimStart('/') ?? "";
            if (_responses.TryGetValue(path, out var body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private const string LocalesJson = """[{"tag":"en","displayName":"English"},{"tag":"fr-FR","displayName":"Français"}]""";
    private const string EnJson = """{"nav.bands":"Bands","greeting":"Hello","with.arg":"{0} says hello"}""";
    private const string FrJson = """{"nav.bands":"Groupes","greeting":"Bonjour","with.arg":"{0} dit bonjour"}""";
    private const string FrJsonMissingKey = """{"nav.bands":"Groupes"}""";

    private static (LocalizationService, FakeJSRuntime, StubHttpHandler) Create()
    {
        var js = new FakeJSRuntime();
        var handler = new StubHttpHandler();
        handler.Set("i18n/locales.json", LocalesJson);
        handler.Set("i18n/en.json", EnJson);
        handler.Set("i18n/fr-fr.json", FrJson);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var svc = new LocalizationService(http, js, NullLogger<LocalizationService>.Instance);
        return (svc, js, handler);
    }

    [Fact]
    public async Task EnsureLoadedAsync_IsIdempotent()
    {
        var (svc, _, handler) = Create();

        await svc.EnsureLoadedAsync();
        await svc.EnsureLoadedAsync();

        Assert.Equal("en", svc.CurrentLocale);
        Assert.Equal(2, svc.AvailableLocales.Count);
    }

    [Fact]
    public async Task EnsureLoadedAsync_NoPersistedTag_AutodetectsBrowserLanguage()
    {
        var (svc, js, _) = Create();
        js.ReturnValues["festivalRiderI18n.getNavigatorLanguage"] = "fr-FR";

        await svc.EnsureLoadedAsync();

        Assert.Equal("fr-FR", svc.CurrentLocale);
        Assert.Equal("Bonjour", svc.T("greeting"));
    }

    [Fact]
    public async Task EnsureLoadedAsync_NoPersistedTag_UnknownLanguage_FallsBackToEn()
    {
        var (svc, js, _) = Create();
        js.ReturnValues["festivalRiderI18n.getNavigatorLanguage"] = "zh-CN";

        await svc.EnsureLoadedAsync();

        Assert.Equal("en", svc.CurrentLocale);
        Assert.Equal("Hello", svc.T("greeting"));
    }

    [Fact]
    public async Task EnsureLoadedAsync_PersistedTag_UsesIt()
    {
        var (svc, js, _) = Create();
        js.ReturnValues["festivalRiderStorage.getItem"] = "fr-FR";

        await svc.EnsureLoadedAsync();

        Assert.Equal("fr-FR", svc.CurrentLocale);
        Assert.Equal("Bonjour", svc.T("greeting"));
    }

    [Fact]
    public async Task SetLocaleAsync_RaisesOnLocaleChanged_AndPersists()
    {
        var (svc, js, _) = Create();
        await svc.EnsureLoadedAsync();

        var fired = 0;
        svc.OnLocaleChanged += () => fired++;

        await svc.SetLocaleAsync("fr-FR");

        Assert.Equal(1, fired);
        Assert.Equal("fr-FR", svc.CurrentLocale);
        Assert.Equal("Bonjour", svc.T("greeting"));
        Assert.Contains(js.Invocations, kvp =>
            kvp.Key == "festivalRiderStorage.setItem" &&
            kvp.Value.Any(a => a.Length >= 2 && (a[1] as string) == "fr-FR"));
    }

    [Fact]
    public async Task SetLocaleAsync_SameTag_IsNoOp()
    {
        var (svc, _, _) = Create();
        await svc.EnsureLoadedAsync();

        var fired = 0;
        svc.OnLocaleChanged += () => fired++;

        await svc.SetLocaleAsync("en");

        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task T_MissingKeyInActiveLocale_FallsBackToEnglish()
    {
        var (svc, js, handler) = Create();
        handler.Set("i18n/fr-fr.json", FrJsonMissingKey);
        js.ReturnValues["festivalRiderStorage.getItem"] = "fr-FR";

        await svc.EnsureLoadedAsync();

        Assert.Equal("fr-FR", svc.CurrentLocale);
        Assert.Equal("Hello", svc.T("greeting"));
        Assert.Equal("Groupes", svc.T("nav.bands"));
    }

    [Fact]
    public async Task T_FormatsPositionalArgs()
    {
        var (svc, _, _) = Create();
        await svc.EnsureLoadedAsync();

        Assert.Equal("Bob says hello", svc.T("with.arg", "Bob"));
    }

    [Fact]
    public async Task Culture_MatchesActiveTag()
    {
        var (svc, js, _) = Create();
        js.ReturnValues["festivalRiderStorage.getItem"] = "fr-FR";

        await svc.EnsureLoadedAsync();

        Assert.Equal("fr-FR", svc.Culture.Name);
    }
}
