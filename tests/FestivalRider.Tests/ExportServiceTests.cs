using FestivalRider.Models;
using FestivalRider.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FestivalRider.Tests;

public sealed class ExportServiceTests
{
    private static ExportService Create(BandService? bands = null)
    {
        bands ??= new BandService(NullLogger<BandService>.Instance);
        return new ExportService(NullLogger<ExportService>.Instance, bands);
    }

    [Fact]
    public void BandCsv_RoundTrip()
    {
        var bands = new BandService(NullLogger<BandService>.Instance);
        var svc = Create(bands);

        var original = TestDataFactory.FullBand();
        bands.AddBand(original);

        var csv = svc.ExportBandCsv(original);
        Assert.Contains("Section,Key,Value,Index,Notes", csv);
        Assert.Contains("Band,Id", csv);
        Assert.Contains(original.Id.ToString(), csv);

        var imported = svc.ImportBandCsv(csv);

        Assert.Equal(original.Id, imported.Id);
        Assert.Equal(original.Name, imported.Name);
        Assert.Equal(original.Notes, imported.Notes);
        Assert.Equal(original.CreatedAt, imported.CreatedAt);
        Assert.Equal(original.UpdatedAt, imported.UpdatedAt);
        Assert.Single(imported.Contacts);
        Assert.Equal(ContactRole.TourManager, imported.Contacts[0].Role);
        Assert.Equal("Alice", imported.Contacts[0].Name);
        Assert.Single(imported.TravelParty.Members);
        Assert.Equal("Drums", imported.TravelParty.Members[0].Role);
        Assert.Single(imported.Rider.Tech.Cables);
        Assert.Equal(CablePoint.StageCenter, imported.Rider.Tech.Cables[0].Source);
        Assert.Single(imported.Rider.Tech.Lighting.FloorMachines);
        Assert.Equal(4, imported.Rider.Tech.Lighting.FloorMachines[0].Count);
        Assert.Equal(PowerAmperage._63_A, imported.Rider.Tech.Power.Amperage);
        Assert.Equal(PowerPhase.ThreePhase, imported.Rider.Tech.Power.Phase);
        Assert.Equal("Yamaha QL5", imported.Rider.Tech.Foh.OwnConsoleModel);
        Assert.Single(imported.Rider.Tech.Monitors.Wedges);
        Assert.Single(imported.Rider.Tech.Monitors.InEars);
        Assert.Single(imported.Rider.Tech.Stage.Risers);
        Assert.Single(imported.Rider.Tech.Stage.WirelessMics);
        Assert.Equal("Clean towels", imported.Rider.Hospitality.DressingRoomNotes);
    }

    [Fact]
    public void ShowCsv_RoundTrip()
    {
        var svc = Create();
        var original = TestDataFactory.FullShow();
        var csv = svc.ExportShowCsv(original);
        Assert.Contains("Show,DateOfOpening,2024-06-15", csv);
        Assert.Contains("Show.Stage,Id,1,0", csv);
        Assert.Contains("Show.Stage,Name,Main,0", csv);

        var imported = svc.ImportShowCsv(csv);
        Assert.Equal(original.Name, imported.Name);
        Assert.Equal(original.Address, imported.Address);
        Assert.Equal(original.DateOfOpening, imported.DateOfOpening);
        Assert.Equal(original.ShowDayCount, imported.ShowDayCount);
        Assert.Equal(2, imported.Stages.Count);
        Assert.Equal("Main", imported.Stages[0].Name);
        Assert.Equal("Acoustic", imported.Stages[1].Name);
    }

    [Fact]
    public void RunningOrderCsv_ExportAndImport()
    {
        var bands = new BandService(NullLogger<BandService>.Instance);
        var svc = Create(bands);
        var show = TestDataFactory.FullShow();
        bands.ReplaceState(new AppState { Shows = new List<ShowData> { show }, ActiveShowId = show.Id });
        var band = TestDataFactory.FullBand();
        bands.AddBand(band);
        var ro = TestDataFactory.FullRunningOrder(show, band);

        var csv = svc.ExportRunningOrderCsv(ro);
        Assert.Contains("ShowId,Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes", csv);
        Assert.Contains(show.Id.ToString(), csv);
        Assert.Contains("14:00", csv);
        Assert.Contains("18:00", csv);
        Assert.Contains("Warmup", csv);

        var imported = svc.ImportRunningOrderCsv(csv, show, bands.Bands);
        Assert.Equal(2, imported.Slots.Count);
        Assert.Contains(imported.Slots, s => s.StageId == 1 && s.SetLengthMinutes == 60);
        Assert.Contains(imported.Slots, s => s.StageId == 2 && s.SetLengthMinutes == 30);
    }

    [Fact]
    public void RunningOrderCsv_ByStage_Slices()
    {
        var bands = new BandService(NullLogger<BandService>.Instance);
        var svc = Create(bands);
        var show = TestDataFactory.FullShow();
        bands.ReplaceState(new AppState { Shows = new List<ShowData> { show }, ActiveShowId = show.Id });
        var band = TestDataFactory.FullBand();
        bands.AddBand(band);
        var ro = TestDataFactory.FullRunningOrder(show, band);

        var csv = svc.ExportRunningOrderByStageCsv(ro, 1);
        Assert.Contains("Main", csv);
        Assert.Contains("18:00", csv);
        Assert.DoesNotContain("Acoustic", csv);
        Assert.DoesNotContain("14:00", csv);
    }

    [Fact]
    public void RunningOrderCsv_ByBand_Slices()
    {
        var bands = new BandService(NullLogger<BandService>.Instance);
        var svc = Create(bands);
        var show = TestDataFactory.FullShow();
        bands.ReplaceState(new AppState { Shows = new List<ShowData> { show }, ActiveShowId = show.Id });
        var band = TestDataFactory.FullBand();
        bands.AddBand(band);
        var ro = TestDataFactory.FullRunningOrder(show, band);

        var csv = svc.ExportRunningOrderByBandCsv(ro, band.Id);
        Assert.Contains("Test Band", csv);
    }
}
