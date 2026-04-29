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
}
