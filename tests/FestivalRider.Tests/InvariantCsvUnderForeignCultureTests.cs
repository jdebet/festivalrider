using System.Globalization;
using FestivalRider.Models;
using FestivalRider.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FestivalRider.Tests;

public sealed class InvariantCsvUnderForeignCultureTests
{
    private sealed class ScopedCulture : IDisposable
    {
        private readonly CultureInfo _prevCulture;
        private readonly CultureInfo _prevUiCulture;

        public ScopedCulture(CultureInfo culture)
        {
            _prevCulture = CultureInfo.CurrentCulture;
            _prevUiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _prevCulture;
            CultureInfo.CurrentUICulture = _prevUiCulture;
        }
    }

    private static readonly CultureInfo DeDe = CultureInfo.GetCultureInfo("de-DE");

    private static ExportService MakeExport(out BandService bands)
    {
        bands = new BandService(NullLogger<BandService>.Instance);
        return new ExportService(NullLogger<ExportService>.Instance, bands);
    }

    [Fact]
    public void BandCsv_IsByteStableUnderDeDe()
    {
        var svc = MakeExport(out var bands);
        var original = TestDataFactory.FullBand();
        bands.AddBand(original);
        var baseline = svc.ExportBandCsv(original);

        string foreign;
        using (new ScopedCulture(DeDe))
            foreign = svc.ExportBandCsv(original);

        Assert.Equal(baseline, foreign);
    }

    [Fact]
    public void BandCsv_RoundTrip_IsCorrectUnderDeDe()
    {
        var svc = MakeExport(out var bands);
        var original = TestDataFactory.FullBand();
        bands.AddBand(original);
        var csv = svc.ExportBandCsv(original);

        Band imported;
        using (new ScopedCulture(DeDe))
            imported = svc.ImportBandCsv(csv);

        Assert.Equal(original.Id, imported.Id);
        Assert.Equal(original.Name, imported.Name);
        Assert.Equal(original.Rider.Tech.Foh.FootprintWidthMeters,
            imported.Rider.Tech.Foh.FootprintWidthMeters);
        Assert.Equal(original.Rider.Tech.Foh.FootprintLengthMeters,
            imported.Rider.Tech.Foh.FootprintLengthMeters);
        Assert.Equal(original.Rider.Tech.Stage.Risers[0].WidthMeters,
            imported.Rider.Tech.Stage.Risers[0].WidthMeters);
    }

    [Fact]
    public void ShowCsv_IsByteStableUnderDeDe()
    {
        var svc = MakeExport(out _);
        var original = TestDataFactory.FullShow();
        var baseline = svc.ExportShowCsv(original);

        string foreign;
        using (new ScopedCulture(DeDe))
        {
            var svc2 = MakeExport(out _);
            foreign = svc2.ExportShowCsv(original);
        }

        Assert.Equal(baseline, foreign);
    }

    [Fact]
    public void ShowCsv_RoundTrip_IsCorrectUnderDeDe()
    {
        var svc = MakeExport(out _);
        var original = TestDataFactory.FullShow();
        var csv = svc.ExportShowCsv(original);

        ShowData imported;
        using (new ScopedCulture(DeDe))
            imported = svc.ImportShowCsv(csv);

        Assert.Equal(original.Name, imported.Name);
        Assert.Equal(original.DateOfOpening, imported.DateOfOpening);
        Assert.Equal(original.ShowDayCount, imported.ShowDayCount);
    }

    [Fact]
    public void RunningOrderCsv_IsByteStableUnderDeDe()
    {
        var svc = MakeExport(out var bands);
        var show = TestDataFactory.FullShow();
        var band = TestDataFactory.FullBand();
        bands.AddBand(band);
        var ro = TestDataFactory.FullRunningOrder(show, band);
        var baseline = svc.ExportRunningOrderCsv(ro);

        string foreign;
        using (new ScopedCulture(DeDe))
            foreign = svc.ExportRunningOrderCsv(ro);

        Assert.Equal(baseline, foreign);
    }
}
