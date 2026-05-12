using System.Text.Json;
using FestivalRider.Migrators;
using FestivalRider.Models;
using FestivalRider.Services;
using FestivalRider.Tests.Migrators;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FestivalRider.Tests;

public sealed class StorageServiceTests
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static (StorageService, FakeJSRuntime, FakeToastService, FakeTimeProvider, BandService) Create(
        IEnumerable<IStateMigrator>? migrators = null)
    {
        var js = new FakeJSRuntime();
        var toasts = new FakeToastService();
        var bands = new BandService(NullLogger<BandService>.Instance);
        var time = new FakeTimeProvider();
        var svc = new StorageService(NullLogger<StorageService>.Instance, bands, js, toasts, FakeLocalizationService.Instance, time, migrators);
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

    private static string? LastStateWrite(FakeJSRuntime js)
    {
        if (!js.Invocations.TryGetValue("festivalRiderStorage.setItem", out var calls)) return null;
        for (int i = calls.Count - 1; i >= 0; i--)
        {
            var c = calls[i];
            if (c.Length >= 2 && (c[0] as string) == "festivalrider.state") return c[1] as string;
        }
        return null;
    }

    [Fact]
    public async Task Migration_v1_to_v4_Succeeds_PersistsAndToasts()
    {
        var migrators = new IStateMigrator[] { new V1ToV2Migrator(), new V2ToV3Migrator(), new V3ToV4Migrator() };
        var (svc, js, toasts, _, bands) = Create(migrators);
        js.ReturnValues["festivalRiderStorage.getItem"] = TestDataFactory.BuildV1JsonPayload();
        js.ReturnValues["festivalRiderStorage.setItem"] = true;

        await svc.EnsureLoadedAsync();

        Assert.Contains(toasts.Messages, t => t.Text.Contains("Migrated data v1") && t.Text.Contains("v4"));
        // Migration warnings surfaced (genre + inputs + backline).
        Assert.Contains(toasts.Messages, t => t.Text.Contains("Genre"));
        Assert.Contains(toasts.Messages, t => t.Text.Contains("Inputs"));

        // Migrated payload was persisted with schemaVersion=4.
        var persisted = LastStateWrite(js);
        Assert.NotNull(persisted);
        using var doc = JsonDocument.Parse(persisted!);
        Assert.Equal(4, doc.RootElement.GetProperty("schemaVersion").GetInt32());

        // Bands survive the migration.
        Assert.Single(bands.Bands);
        Assert.Equal("Alpha", bands.Bands[0].Name);
    }

    [Fact]
    public async Task Migration_MissingChain_FallsBackToBackupAndReset()
    {
        // Only v1->v2 registered, but CurrentSchemaVersion=3, so chain cannot reach target.
        var migrators = new IStateMigrator[] { new V1ToV2Migrator() };
        var (svc, js, toasts, _, _) = Create(migrators);
        js.ReturnValues["festivalRiderStorage.getItem"] = TestDataFactory.BuildV1JsonPayload();

        await svc.EnsureLoadedAsync();

        Assert.Contains(toasts.Messages, t => t.Text.Contains("schema v1", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(toasts.Messages, t => t.Text.Contains("Migrated data"));
        // Backup written under the v{found} key.
        Assert.Contains(js.Invocations["festivalRiderStorage.setItem"],
            args => args.Length >= 1 && (args[0] as string) == "festivalrider.backup.v1");
    }

    [Fact]
    public async Task Migration_ThrowingMigrator_FallsBackToBackupAndReset()
    {
        var migrators = new IStateMigrator[] { new ThrowingMigrator { FromVersion = 1 }, new V2ToV3Migrator() };
        var (svc, js, toasts, _, _) = Create(migrators);
        js.ReturnValues["festivalRiderStorage.getItem"] = TestDataFactory.BuildV1JsonPayload();

        await svc.EnsureLoadedAsync();

        Assert.Contains(toasts.Messages, t => t.Text.Contains("schema v1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(js.Invocations["festivalRiderStorage.setItem"],
            args => args.Length >= 1 && (args[0] as string) == "festivalrider.backup.v1");
    }

    [Fact]
    public async Task Migration_SecondEnsureLoaded_IsNoOp()
    {
        var migrators = new IStateMigrator[] { new V1ToV2Migrator(), new V2ToV3Migrator() };
        var (svc, js, toasts, _, _) = Create(migrators);
        js.ReturnValues["festivalRiderStorage.getItem"] = TestDataFactory.BuildV1JsonPayload();
        js.ReturnValues["festivalRiderStorage.setItem"] = true;

        await svc.EnsureLoadedAsync();
        var firstCount = toasts.Messages.Count;

        await svc.EnsureLoadedAsync();
        Assert.Equal(firstCount, toasts.Messages.Count);
    }

    [Fact]
    public void Migration_DuplicateFromVersion_ThrowsAtConstruction()
    {
        var migrators = new IStateMigrator[] { new V1ToV2Migrator(), new V1ToV2Migrator() };
        Assert.Throws<InvalidOperationException>(() => Create(migrators));
    }

    [Fact]
    public void Migration_NonStepwise_ThrowsAtConstruction()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Create(new IStateMigrator[] { new NonStepwiseMigrator() }));
    }

    private sealed class NonStepwiseMigrator : IStateMigrator
    {
        public int FromVersion => 1;
        public int ToVersion => 3;
        public System.Text.Json.Nodes.JsonNode Migrate(System.Text.Json.Nodes.JsonNode raw, IList<string> warnings) => raw;
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
