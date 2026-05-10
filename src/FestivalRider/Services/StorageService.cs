using System.Text.Json;
using System.Text.Json.Nodes;
using FestivalRider.Migrators;
using FestivalRider.Models;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace FestivalRider.Services;

public class StorageService : IStorageService, IAsyncDisposable
{
    private const string StateKey = "festivalrider.state";
    private const string LockKey = "festivalrider.tab-lock";
    private const string BackupKeyPrefix = "festivalrider.backup.v";
    private const int CurrentSchemaVersion = 3;
    private const int DebounceMs = 1000;
    private const int HeartbeatMs = 2000;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<StorageService> _logger;
    private readonly IBandService _bands;
    private readonly IJSRuntime _js;
    private readonly IToastService _toasts;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyDictionary<int, IStateMigrator> _migrators;
    private readonly Guid _tabId = Guid.NewGuid();

    private DotNetObjectReference<StorageService>? _selfRef;
    private CancellationTokenSource? _debounceCts;
    private ITimer? _heartbeatTimer;
    private bool _loaded;
    private bool _subscribed;

    public StorageService(
        ILogger<StorageService> logger,
        IBandService bands,
        IJSRuntime js,
        IToastService toasts,
        TimeProvider? timeProvider = null,
        IEnumerable<IStateMigrator>? migrators = null)
    {
        _logger = logger;
        _bands = bands;
        _js = js;
        _toasts = toasts;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _migrators = BuildMigratorIndex(migrators);
    }

    private static IReadOnlyDictionary<int, IStateMigrator> BuildMigratorIndex(IEnumerable<IStateMigrator>? migrators)
    {
        var dict = new Dictionary<int, IStateMigrator>();
        if (migrators is null) return dict;
        foreach (var m in migrators)
        {
            if (m.ToVersion != m.FromVersion + 1)
                throw new InvalidOperationException(
                    $"Migrator {m.GetType().Name} is not step-wise: FromVersion={m.FromVersion}, ToVersion={m.ToVersion}.");
            if (!dict.TryAdd(m.FromVersion, m))
                throw new InvalidOperationException(
                    $"Duplicate IStateMigrator registered for FromVersion={m.FromVersion}: {dict[m.FromVersion].GetType().Name} and {m.GetType().Name}.");
        }
        return dict;
    }

    public bool AnotherTabActive { get; private set; }

    public event Action? OnAnotherTabChanged;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;

        _selfRef = DotNetObjectReference.Create(this);
        try
        {
            await _js.InvokeAsync<bool>("festivalRiderStorage.registerBeforeUnload", _selfRef, nameof(OnBeforeUnload));
            await _js.InvokeAsync<bool>("festivalRiderStorage.registerStorageEvent", _selfRef, nameof(OnStorageEvent));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register JS callbacks; persistence still works without unload flush.");
        }

        await LoadStateAsync();

        _bands.OnChange += OnBandsChanged;
        _subscribed = true;

        _heartbeatTimer = _timeProvider.CreateTimer(_ => _ = HeartbeatAsync(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(HeartbeatMs));
    }

    public async Task FlushAsync()
    {
        _debounceCts?.Cancel();
        await WriteStateAsync();
    }

    public async Task ClearAsync()
    {
        _debounceCts?.Cancel();
        try { await _js.InvokeVoidAsync("festivalRiderStorage.removeItem", StateKey); }
        catch (Exception ex) { _logger.LogWarning(ex, "Clear failed."); }
        _bands.ReplaceState(new AppState());
    }

    private async Task LoadStateAsync()
    {
        string? raw;
        try { raw = await _js.InvokeAsync<string?>("festivalRiderStorage.getItem", StateKey); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to read state from localStorage."); return; }

        if (string.IsNullOrWhiteSpace(raw)) return;

        int foundVersion = 0;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("schemaVersion", out var v) && v.ValueKind == JsonValueKind.Number)
                foundVersion = v.GetInt32();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "State payload is not valid JSON; backing up and resetting.");
            await BackupAndResetAsync(raw, foundVersion: 0);
            _toasts.Show("Saved data was unreadable; reset to a clean state.", ToastLevel.Warning);
            return;
        }

        if (foundVersion != CurrentSchemaVersion)
        {
            var migrated = TryMigrate(raw, foundVersion, out var migratedJson, out var warnings);
            if (!migrated)
            {
                await BackupAndResetAsync(raw, foundVersion);
                _toasts.Show($"Saved data uses schema v{foundVersion}; backed up and reset to v{CurrentSchemaVersion}.", ToastLevel.Warning);
                return;
            }

            // Persist migrated payload BEFORE binding so a crash mid-load doesn't loop.
            try
            {
                var ok = await _js.InvokeAsync<bool>("festivalRiderStorage.setItem", StateKey, migratedJson!);
                if (!ok)
                {
                    _logger.LogWarning("Persisting migrated payload returned false; falling back to backup-and-reset.");
                    await BackupAndResetAsync(raw, foundVersion);
                    _toasts.Show("Migration could not be saved; backed up and reset.", ToastLevel.Error);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist migrated payload; falling back to backup-and-reset.");
                await BackupAndResetAsync(raw, foundVersion);
                _toasts.Show("Migration could not be saved; backed up and reset.", ToastLevel.Error);
                return;
            }

            raw = migratedJson!;
            _toasts.Show($"Migrated data v{foundVersion} \u2192 v{CurrentSchemaVersion}.", ToastLevel.Info);
            foreach (var w in warnings.Take(3))
                _toasts.Show(w, ToastLevel.Warning);
            if (warnings.Count > 0)
                _logger.LogInformation("Migration warnings ({Count}): {All}", warnings.Count, string.Join(" | ", warnings));
        }

        try
        {
            var state = JsonSerializer.Deserialize<AppState>(raw, JsonOpts);
            if (state is null)
            {
                _toasts.Show("Saved data was empty; reset to a clean state.", ToastLevel.Warning);
                return;
            }
            _bands.ReplaceState(state);
            _toasts.Show($"Restored {state.Bands.Count} bands from previous session.", ToastLevel.Info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize state; backing up and resetting.");
            await BackupAndResetAsync(raw, foundVersion);
            _toasts.Show("Saved data was unreadable; reset to a clean state.", ToastLevel.Warning);
        }
    }

    private bool TryMigrate(string raw, int foundVersion, out string? migratedJson, out List<string> warnings)
    {
        migratedJson = null;
        warnings = new List<string>();
        if (_migrators.Count == 0) return false;

        // Pre-flight: chain must reach CurrentSchemaVersion without gaps.
        for (int v = foundVersion; v < CurrentSchemaVersion; v++)
            if (!_migrators.ContainsKey(v)) return false;

        JsonNode node;
        try
        {
            node = JsonNode.Parse(raw) ?? throw new InvalidOperationException("Parsed payload is null.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse raw payload as JsonNode for migration.");
            return false;
        }

        try
        {
            for (int v = foundVersion; v < CurrentSchemaVersion; v++)
            {
                var migrator = _migrators[v];
                node = migrator.Migrate(node, warnings)
                    ?? throw new InvalidOperationException($"Migrator {migrator.GetType().Name} returned null.");
                if (node is not JsonObject obj)
                    throw new InvalidOperationException($"Migrator {migrator.GetType().Name} did not return a JSON object root.");
                obj["schemaVersion"] = migrator.ToVersion;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migrator threw; falling back to backup-and-reset.");
            return false;
        }

        if (node is not JsonObject finalRoot || finalRoot["schemaVersion"] is null)
        {
            _logger.LogError("Migrated payload is missing 'schemaVersion'; falling back to backup-and-reset.");
            return false;
        }

        migratedJson = node.ToJsonString(JsonOpts);
        return true;
    }

    private async Task BackupAndResetAsync(string raw, int foundVersion)
    {
        try { await _js.InvokeVoidAsync("festivalRiderStorage.setItem", $"{BackupKeyPrefix}{foundVersion}", raw); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to write backup payload."); }
        try { await _js.InvokeVoidAsync("festivalRiderStorage.removeItem", StateKey); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to remove stale state."); }
        _bands.ReplaceState(new AppState());
    }

    private void OnBandsChanged() => _ = ScheduleWriteAsync();

    private async Task ScheduleWriteAsync()
    {
        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        try { await Task.Delay(TimeSpan.FromMilliseconds(DebounceMs), _timeProvider, cts.Token); }
        catch (TaskCanceledException) { return; }
        if (cts.IsCancellationRequested) return;
        await WriteStateAsync();
    }

    private async Task WriteStateAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_bands.Snapshot(), JsonOpts);
            var ok = await _js.InvokeAsync<bool>("festivalRiderStorage.setItem", StateKey, json);
            if (!ok)
                _toasts.Show("Saving failed (storage quota?). Export to CSV, then clear data.", ToastLevel.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write state.");
        }
    }

    private async Task HeartbeatAsync()
    {
        try
        {
            var payload = JsonSerializer.Serialize(new LockEntry(_tabId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), JsonOpts);
            await _js.InvokeAsync<bool>("festivalRiderStorage.setItem", LockKey, payload);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Heartbeat write failed.");
        }
    }

    private void SetAnotherTab(bool value)
    {
        if (AnotherTabActive == value) return;
        AnotherTabActive = value;
        OnAnotherTabChanged?.Invoke();
    }

    [JSInvokable]
    public async Task OnBeforeUnload()
    {
        _debounceCts?.Cancel();
        try
        {
            var json = JsonSerializer.Serialize(_bands.Snapshot(), JsonOpts);
            await _js.InvokeAsync<bool>("festivalRiderStorage.setItem", StateKey, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "beforeunload flush failed.");
        }
        try { await _js.InvokeVoidAsync("festivalRiderStorage.removeItem", LockKey); }
        catch (Exception ex) { _logger.LogDebug(ex, "beforeunload lock release failed."); }
    }

    [JSInvokable]
    public void OnStorageEvent(string key, string? newValue)
    {
        if (key != LockKey) return;
        if (string.IsNullOrWhiteSpace(newValue))
        {
            SetAnotherTab(false);
            return;
        }
        try
        {
            var entry = JsonSerializer.Deserialize<LockEntry>(newValue, JsonOpts);
            if (entry is null) return;
            if (entry.TabId == _tabId) return;
            SetAnotherTab(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Storage event parse failed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscribed) _bands.OnChange -= OnBandsChanged;
        _heartbeatTimer?.Dispose();
        _debounceCts?.Cancel();
        try { await FlushAsync(); } catch { /* swallowed during disposal */ }
        try { await _js.InvokeVoidAsync("festivalRiderStorage.removeItem", LockKey); }
        catch { /* swallowed during disposal */ }
        _selfRef?.Dispose();
    }

    private sealed record LockEntry(Guid TabId, long Ts);
}
