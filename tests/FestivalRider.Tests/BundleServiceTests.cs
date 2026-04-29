using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FestivalRider.Models;
using FestivalRider.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FestivalRider.Tests;

public sealed class BundleServiceTests
{
    private static (BundleService, IExportService, BandService) Create()
    {
        var bands = new BandService(NullLogger<BandService>.Instance);
        var export = new ExportService(NullLogger<ExportService>.Instance, bands);
        var svc = new BundleService(export, NullLogger<BundleService>.Instance);
        return (svc, export, bands);
    }

    private static AppState FullState()
    {
        var state = new AppState();
        state.ShowData = TestDataFactory.FullShow();
        state.Bands.Add(TestDataFactory.FullBand(Guid.NewGuid()));
        state.Bands.Add(TestDataFactory.FullBand(Guid.NewGuid()));
        state.RunningOrders.Add(new RunningOrder
        {
            Id = Guid.NewGuid(),
            ShowDayNumber = 1,
            Slots =
            {
                new(state.Bands[0].Id, 1, new TimeOnly(18, 0), 60, 15, "Headliner"),
                new(state.Bands[1].Id, 2, new TimeOnly(14, 0), 30, 10, "Warmup"),
            }
        });
        return state;
    }

    private static byte[] RebuildZip(byte[] zip, Func<string, string, string> transform, IEnumerable<(string name, string content)>? extras = null)
    {
        var entries = new List<(string name, string content)>();
        using (var ms = new MemoryStream(zip))
        using (var zipIn = new ZipArchive(ms, ZipArchiveMode.Read))
        {
            foreach (var entry in zipIn.Entries)
            {
                using var s = entry.Open();
                using var reader = new StreamReader(s);
                entries.Add((entry.FullName, transform(entry.FullName, reader.ReadToEnd())));
            }
        }
        if (extras is not null) entries.AddRange(extras);

        using var outMs = new MemoryStream();
        using (var outZip = new ZipArchive(outMs, ZipArchiveMode.Create, true))
        {
            foreach (var (name, content) in entries)
            {
                var ne = outZip.CreateEntry(name);
                using var ws = ne.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                ws.Write(bytes, 0, bytes.Length);
            }
        }
        return outMs.ToArray();
    }

    [Fact]
    public void ExportThenImport_RoundTrips()
    {
        var (svc, _, bands) = Create();
        var original = FullState();
        bands.ReplaceState(original);

        var zip = svc.ExportBundle(original);
        Assert.NotNull(zip);
        Assert.True(zip.Length > 0);

        var result = svc.ImportBundle(new MemoryStream(zip));
        Assert.Null(result.Error);
        Assert.Equal(2, result.BandCount);
        Assert.Equal(1, result.RunningOrderCount);
        Assert.Equal(original.Bands[0].Name, result.State!.Bands[0].Name);
        Assert.Equal(original.Bands[0].Rider.Tech.Foh.OwnConsoleModel, result.State.Bands[0].Rider.Tech.Foh.OwnConsoleModel);
        Assert.Equal(original.ShowData.Name, result.State.ShowData.Name);
        Assert.Equal(original.RunningOrders[0].Slots.Count, result.State.RunningOrders[0].Slots.Count);
        Assert.Null(result.Merge);
    }

    [Fact]
    public void Export_ContainsManifestAndFiles()
    {
        var (svc, _, bands) = Create();
        var original = FullState();
        bands.ReplaceState(original);

        var zip = svc.ExportBundle(bands.Snapshot());
        using var ms = new MemoryStream(zip);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var names = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("manifest.json", names);
        Assert.Contains("show.csv", names);
        Assert.Equal(2, names.Count(n => n.StartsWith("bands/", StringComparison.Ordinal)));
        Assert.Equal(1, names.Count(n => n.StartsWith("running-orders/", StringComparison.Ordinal)));
    }

    [Fact]
    public void Import_SchemaMismatch_Refuses()
    {
        var (svc, _, bands) = Create();
        var original = FullState();
        bands.ReplaceState(original);
        var zip = svc.ExportBundle(original);

        var tampered = RebuildZip(zip, (name, content) =>
        {
            if (name != "manifest.json") return content;
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content)!;
            var copy = dict.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            copy["schemaVersion"] = 999;
            return JsonSerializer.Serialize(copy);
        });

        var result = svc.ImportBundle(new MemoryStream(tampered));
        Assert.NotNull(result.Error);
        Assert.Contains("schema", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_ManifestTampered_Refuses()
    {
        var (svc, _, bands) = Create();
        var original = FullState();
        bands.ReplaceState(original);
        var zip = svc.ExportBundle(original);

        var tampered = RebuildZip(zip, (name, content) =>
        {
            if (name != "manifest.json") return content;
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content)!;
            var copy = dict.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            copy["format"] = "tampered";
            return JsonSerializer.Serialize(copy);
        });

        var result = svc.ImportBundle(new MemoryStream(tampered));
        Assert.NotNull(result.Error);
        Assert.Contains("format", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_UnlistedEntry_Ignored()
    {
        var (svc, _, bands) = Create();
        var original = FullState();
        bands.ReplaceState(original);
        var zip = svc.ExportBundle(original);

        var tampered = RebuildZip(zip, (_, content) => content,
            extras: new[] { ("unlisted.txt", "extra") });

        var result = svc.ImportBundle(new MemoryStream(tampered));
        Assert.Null(result.Error);
        Assert.Contains(result.Warnings, w => w.Contains("unlisted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Import_EmptyZip_Fails()
    {
        var (svc, _, _) = Create();
        var result = svc.ImportBundle(new MemoryStream(Array.Empty<byte>()));
        Assert.NotNull(result.Error);
    }

    // -------- Merge mode --------

    private static AppState BuildLocalState(Action<AppState>? configure = null)
    {
        var state = new AppState
        {
            ShowData = TestDataFactory.FullShow(), // stages: 1=Main, 2=Acoustic
        };
        configure?.Invoke(state);
        return state;
    }

    private static Band BandWith(Guid id, string name, DateTimeOffset updatedAt)
    {
        var b = TestDataFactory.FullBand(id);
        b.Name = name;
        b.UpdatedAt = updatedAt;
        return b;
    }

    [Fact]
    public void Merge_Upserts_AddsAndPreservesUnlisted()
    {
        var (svc, _, _) = Create();

        // Bundle carries one band (incoming Guid not present locally).
        var bundleSender = new BandService(NullLogger<BandService>.Instance);
        var bundleState = new AppState
        {
            ShowData = TestDataFactory.FullShow(),
            Bands = { BandWith(Guid.NewGuid(), "Incoming", new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero)) },
        };
        bundleSender.ReplaceState(bundleState);
        var zip = svc.ExportBundle(bundleState);

        // Local has a different band.
        var localOnlyId = Guid.NewGuid();
        var local = BuildLocalState(s => s.Bands.Add(BandWith(localOnlyId, "Local", DateTimeOffset.UtcNow)));

        var result = svc.ImportBundle(new MemoryStream(zip), BundleImportMode.Merge, local);

        Assert.Null(result.Error);
        Assert.NotNull(result.Merge);
        Assert.Equal(1, result.Merge!.BandsAdded);
        Assert.Equal(0, result.Merge.BandsUpdated);
        Assert.Equal(0, result.Merge.BandsSkipped);
        Assert.Equal(1, result.BandCount);
        Assert.Equal(2, result.State!.Bands.Count);
        Assert.Contains(result.State.Bands, b => b.Id == localOnlyId);
        Assert.Contains(result.State.Bands, b => b.Name == "Incoming");
    }

    [Fact]
    public void Merge_Bands_LastWriteWins_ByUpdatedAt()
    {
        var (svc, _, _) = Create();
        var sharedId = Guid.NewGuid();

        var olderBundle = new AppState
        {
            ShowData = TestDataFactory.FullShow(),
            Bands = { BandWith(sharedId, "OldName", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)) },
        };
        var zipOlder = svc.ExportBundle(olderBundle);

        var local = BuildLocalState(s => s.Bands.Add(
            BandWith(sharedId, "LocalName", new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero))));

        var skipResult = svc.ImportBundle(new MemoryStream(zipOlder), BundleImportMode.Merge, local);
        Assert.Null(skipResult.Error);
        Assert.Equal(1, skipResult.Merge!.BandsSkipped);
        Assert.Equal(0, skipResult.Merge.BandsUpdated);
        Assert.Equal("LocalName", skipResult.State!.Bands.Single(b => b.Id == sharedId).Name);
        Assert.Contains(skipResult.Warnings, w => w.Contains("skipped", StringComparison.OrdinalIgnoreCase));

        // Newer incoming wins.
        var newerBundle = new AppState
        {
            ShowData = TestDataFactory.FullShow(),
            Bands = { BandWith(sharedId, "NewerName", new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero)) },
        };
        var zipNewer = svc.ExportBundle(newerBundle);
        var updateResult = svc.ImportBundle(new MemoryStream(zipNewer), BundleImportMode.Merge, local);
        Assert.Equal(1, updateResult.Merge!.BandsUpdated);
        Assert.Equal(0, updateResult.Merge.BandsSkipped);
        Assert.Equal("NewerName", updateResult.State!.Bands.Single(b => b.Id == sharedId).Name);
    }

    [Fact]
    public void Merge_Bands_EqualUpdatedAt_TreatedAsSkip()
    {
        var (svc, _, _) = Create();
        var sharedId = Guid.NewGuid();
        var ts = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var bundle = new AppState
        {
            ShowData = TestDataFactory.FullShow(),
            Bands = { BandWith(sharedId, "Incoming", ts) },
        };
        var zip = svc.ExportBundle(bundle);
        var local = BuildLocalState(s => s.Bands.Add(BandWith(sharedId, "Local", ts)));

        var result = svc.ImportBundle(new MemoryStream(zip), BundleImportMode.Merge, local);
        Assert.Equal(1, result.Merge!.BandsSkipped);
        Assert.Equal("Local", result.State!.Bands.Single(b => b.Id == sharedId).Name);
    }

    [Fact]
    public void Merge_DoesNotMutateShowData()
    {
        var (svc, _, _) = Create();

        var bundleShow = TestDataFactory.FullShow();
        bundleShow.Name = "Sender Festival";
        bundleShow.Stages[0].Name = "RenamedMain";
        var bundle = new AppState { ShowData = bundleShow };
        var zip = svc.ExportBundle(bundle);

        var local = BuildLocalState();
        var localShowRef = local.ShowData;

        var result = svc.ImportBundle(new MemoryStream(zip), BundleImportMode.Merge, local);
        Assert.Null(result.Error);
        Assert.Same(localShowRef, result.State!.ShowData);
        Assert.Equal("Festival 2024", result.State.ShowData.Name);
        Assert.Equal("Main", result.State.ShowData.Stages[0].Name);
    }

    [Fact]
    public void Merge_RunningOrder_StageRemap_ByName_DifferentLocalIds()
    {
        var (svc, _, bands) = Create();

        // Sender's ShowData uses non-default stage IDs that won't match the local ones.
        var senderShow = new ShowData
        {
            Name = "Sender",
            DateOfOpening = new DateOnly(2024, 6, 15),
            ShowDayCount = 3,
        };
        senderShow.Stages.Add(new Stage { Id = 42, Name = "Main" });
        senderShow.Stages.Add(new Stage { Id = 99, Name = "Acoustic" });

        var bandId = Guid.NewGuid();
        var bundle = new AppState
        {
            ShowData = senderShow,
            Bands = { BandWith(bandId, "B", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)) },
            RunningOrders =
            {
                new RunningOrder
                {
                    Id = Guid.NewGuid(),
                    ShowDayNumber = 1,
                    Slots =
                    {
                        new(bandId, 42, new TimeOnly(18, 0), 60, 15, null),
                        new(bandId, 99, new TimeOnly(14, 0), 30, 10, null),
                    },
                },
            },
        };
        bands.ReplaceState(bundle);
        var zip = svc.ExportBundle(bundle);

        var local = BuildLocalState(); // local stages: 1=Main, 2=Acoustic
        var result = svc.ImportBundle(new MemoryStream(zip), BundleImportMode.Merge, local);

        Assert.Null(result.Error);
        Assert.Equal(1, result.Merge!.RunningOrdersAdded);
        Assert.Equal(0, result.Merge.RunningOrdersSkipped);
        var ro = Assert.Single(result.State!.RunningOrders);
        // CSV export sorts slots by StartTime: 14:00 (Acoustic→2) then 18:00 (Main→1).
        Assert.Equal(new[] { 2, 1 }, ro.Slots.Select(s => s.StageId).ToArray());
    }

    [Fact]
    public void Merge_RunningOrder_StageRemap_Failure_SkipsWholeOrder()
    {
        var (svc, _, bands) = Create();

        var senderShow = new ShowData { Name = "Sender", ShowDayCount = 1 };
        senderShow.Stages.Add(new Stage { Id = 1, Name = "Main" });
        senderShow.Stages.Add(new Stage { Id = 2, Name = "Tent" }); // not present locally

        var bandId = Guid.NewGuid();
        var bundle = new AppState
        {
            ShowData = senderShow,
            Bands = { BandWith(bandId, "B", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)) },
            RunningOrders =
            {
                new RunningOrder
                {
                    Id = Guid.NewGuid(),
                    ShowDayNumber = 1,
                    Slots =
                    {
                        new(bandId, 1, new TimeOnly(18, 0), 60, 15, null),
                        new(bandId, 2, new TimeOnly(20, 0), 60, 15, null),
                    },
                },
            },
        };
        bands.ReplaceState(bundle);
        var zip = svc.ExportBundle(bundle);

        var local = BuildLocalState();
        var result = svc.ImportBundle(new MemoryStream(zip), BundleImportMode.Merge, local);

        Assert.Null(result.Error);
        Assert.Equal(1, result.Merge!.RunningOrdersSkipped);
        Assert.Equal(0, result.Merge.RunningOrdersAdded);
        Assert.Empty(result.State!.RunningOrders);
        Assert.Contains(result.Warnings, w => w.Contains("Tent", StringComparison.Ordinal));
    }

    [Fact]
    public void Merge_RunningOrder_GuidCollision_IncomingWins_WithWarning()
    {
        var (svc, _, bands) = Create();
        var sharedRoId = Guid.NewGuid();
        var bandId = Guid.NewGuid();

        var bundle = new AppState
        {
            ShowData = TestDataFactory.FullShow(),
            Bands = { BandWith(bandId, "B", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)) },
            RunningOrders =
            {
                new RunningOrder
                {
                    Id = sharedRoId,
                    ShowDayNumber = 2,
                    Slots = { new(bandId, 1, new TimeOnly(18, 0), 60, 15, "incoming") },
                },
            },
        };
        bands.ReplaceState(bundle);
        var zip = svc.ExportBundle(bundle);

        var local = BuildLocalState(s =>
        {
            s.Bands.Add(BandWith(bandId, "Local B", DateTimeOffset.UtcNow));
            s.RunningOrders.Add(new RunningOrder
            {
                Id = sharedRoId,
                ShowDayNumber = 1,
                Slots = { new(bandId, 1, new TimeOnly(10, 0), 30, 5, "local") },
            });
        });

        var result = svc.ImportBundle(new MemoryStream(zip), BundleImportMode.Merge, local);
        Assert.Null(result.Error);
        Assert.Equal(1, result.Merge!.RunningOrdersUpdated);
        var ro = Assert.Single(result.State!.RunningOrders);
        Assert.Equal("incoming", ro.Slots[0].Notes);
        Assert.Contains(result.Warnings, w => w.Contains("replaced", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Merge_DeterministicSort_ByGuid()
    {
        var (svc, _, _) = Create();

        var bundle = new AppState
        {
            ShowData = TestDataFactory.FullShow(),
            Bands =
            {
                BandWith(new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), "Z", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                BandWith(new Guid("00000000-0000-0000-0000-000000000001"), "A", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            },
        };
        var zip = svc.ExportBundle(bundle);

        var local = BuildLocalState(s =>
            s.Bands.Add(BandWith(new Guid("88888888-8888-8888-8888-888888888888"), "M", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero))));

        var result = svc.ImportBundle(new MemoryStream(zip), BundleImportMode.Merge, local);
        Assert.Null(result.Error);
        var ids = result.State!.Bands.Select(b => b.Id).ToList();
        Assert.Equal(ids.OrderBy(g => g).ToList(), ids);
    }

    [Fact]
    public void Merge_SchemaMismatch_RefusesLikeReplace()
    {
        var (svc, _, _) = Create();
        var bundle = new AppState { ShowData = TestDataFactory.FullShow() };
        var zip = svc.ExportBundle(bundle);

        var tampered = RebuildZip(zip, (name, content) =>
        {
            if (name != "manifest.json") return content;
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content)!;
            var copy = dict.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            copy["schemaVersion"] = 999;
            return JsonSerializer.Serialize(copy);
        });

        var local = BuildLocalState();
        var result = svc.ImportBundle(new MemoryStream(tampered), BundleImportMode.Merge, local);
        Assert.NotNull(result.Error);
        Assert.Null(result.State);
        Assert.Null(result.Merge);
    }

    [Fact]
    public void Merge_NullCurrentState_Throws()
    {
        var (svc, _, _) = Create();
        var bundle = new AppState { ShowData = TestDataFactory.FullShow() };
        var zip = svc.ExportBundle(bundle);
        Assert.Throws<ArgumentNullException>(
            () => svc.ImportBundle(new MemoryStream(zip), BundleImportMode.Merge, currentState: null));
    }
}
