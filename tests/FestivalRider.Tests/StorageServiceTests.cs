using System.Text.Json;
using FestivalRider.Models;
using FestivalRider.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FestivalRider.Tests;

public sealed class StorageServiceTests
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static (StorageService, FakeJSRuntime, FakeToastService, FakeTimeProvider, BandService) Create()
    {
        var js = new FakeJSRuntime();
        var toasts = new FakeToastService();
        var bands = new BandService(NullLogger<BandService>.Instance);
        var time = new FakeTimeProvider();
        var svc = new StorageService(NullLogger<StorageService>.Instance, bands, js, toasts, time);
        return (svc, js, toasts, time, bands);
    }

    private static int CountStateWrites(FakeJSRuntime js)
    {
        if (!js.Invocations.TryGetValue("festivalRiderStorage.setItem", out var calls)) return 0;
        return calls.Count(c => c.Length > 0 && (c[0] as string) == "festivalrider.state");
    }

    [Fact]
    public async Task EnsureLoadedAsync_IsIdempotent()
    {
        var (svc, js, _, _, _) = Create();

        await svc.EnsureLoadedAsync();
        await svc.EnsureLoadedAsync();

        Assert.Single(js.Invocations["festivalRiderStorage.registerBeforeUnload"]);
        Assert.Single(js.Invocations["festivalRiderStorage.registerStorageEvent"]);
    }

    [Fact]
    public async Task Load_SchemaMismatch_BacksUpAndResets()
    {
        var (svc, js, toasts, _, _) = Create();
        js.ReturnValues["festivalRiderStorage.getItem"] =
            JsonSerializer.Serialize(new AppState { SchemaVersion = 1 }, CamelCase);

        await svc.EnsureLoadedAsync();

        Assert.Contains(js.Invocations, kvp => kvp.Key == "festivalRiderStorage.setItem");
        Assert.Contains(js.Invocations, kvp => kvp.Key == "festivalRiderStorage.removeItem");
        Assert.Contains(toasts.Messages, t => t.Text.Contains("schema v1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Load_InvalidJson_BacksUpAndResets()
    {
        var (svc, js, toasts, _, _) = Create();
        js.ReturnValues["festivalRiderStorage.getItem"] = "not json";

        await svc.EnsureLoadedAsync();

        Assert.Contains(toasts.Messages, t => t.Text.Contains("unreadable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Debounce_DelaysWrite()
    {
        var (svc, js, _, time, bands) = Create();
        await svc.EnsureLoadedAsync();
        var before = CountStateWrites(js);
        bands.AddBand(new Band { Id = Guid.NewGuid(), Name = "A" });

        // immediately after mutation, no new state write yet
        Assert.Equal(before, CountStateWrites(js));

        time.Advance(TimeSpan.FromSeconds(1.5));

        Assert.True(CountStateWrites(js) > before);
    }

    [Fact]
    public async Task Debounce_CancelledBySecondMutation()
    {
        var (svc, js, _, time, bands) = Create();
        await svc.EnsureLoadedAsync();
        var before = CountStateWrites(js);

        bands.AddBand(new Band { Id = Guid.NewGuid(), Name = "A" });
        time.Advance(TimeSpan.FromMilliseconds(500));
        bands.AddBand(new Band { Id = Guid.NewGuid(), Name = "B" });
        time.Advance(TimeSpan.FromMilliseconds(600));

        // first debounce was cancelled; not yet 1s since second mutation
        Assert.Equal(before, CountStateWrites(js));

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(CountStateWrites(js) > before);
    }

    [Fact]
    public async Task Flush_WritesImmediately()
    {
        var (svc, js, _, _, bands) = Create();
        await svc.EnsureLoadedAsync();
        var before = CountStateWrites(js);

        bands.AddBand(new Band { Id = Guid.NewGuid(), Name = "A" });
        await svc.FlushAsync();

        Assert.True(CountStateWrites(js) > before);
    }

    [Fact]
    public async Task OnStorageEvent_SetsAnotherTabActive()
    {
        var (svc, _, _, _, _) = Create();
        await svc.EnsureLoadedAsync();

        var fired = false;
        svc.OnAnotherTabChanged += () => fired = true;

        var payload = JsonSerializer.Serialize(
            new { tabId = Guid.NewGuid(), ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
            CamelCase);
        svc.OnStorageEvent("festivalrider.tab-lock", payload);

        Assert.True(svc.AnotherTabActive);
        Assert.True(fired);
    }

    [Fact]
    public async Task DisposeAsync_Flushes()
    {
        var (svc, js, _, _, bands) = Create();
        await svc.EnsureLoadedAsync();
        var before = CountStateWrites(js);

        bands.AddBand(new Band { Id = Guid.NewGuid(), Name = "A" });
        await svc.DisposeAsync();

        Assert.True(CountStateWrites(js) > before);
    }
}
