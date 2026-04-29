using FestivalRider.Models;
using FestivalRider.PrintStrategies;
using FestivalRider.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FestivalRider.Tests;

public sealed class PrintStrategyTests
{
    private static (IBandService, Band) Seed()
    {
        var bands = new BandService(NullLogger<BandService>.Instance);
        bands.ReplaceState(new AppState
        {
            ShowData = new ShowData
            {
                Name = "Fest 2024",
                DateOfOpening = new DateOnly(2024, 6, 15),
                ShowDayCount = 2,
                Stages = { new Stage { Id = 1, Name = "Main" } }
            }
        });
        var band = TestDataFactory.FullBand();
        bands.AddBand(band);
        bands.AddRunningOrder(new RunningOrder
        {
            Id = Guid.NewGuid(),
            ShowDayNumber = 1,
            Slots = { new(band.Id, 1, new TimeOnly(20, 0), 60, 15, "Headliner") }
        });
        return (bands, band);
    }

    [Fact]
    public void BandRiderPrintStrategy_KeyIsBand()
    {
        var (bands, band) = Seed();
        var strat = new BandRiderPrintStrategy(bands);
        Assert.Equal("band", strat.Key);
    }

    [Fact]
    public void BandRiderPrintStrategy_Title_ContainsBandName()
    {
        var (bands, band) = Seed();
        var strat = new BandRiderPrintStrategy(bands);
        var title = strat.GetTitle(band.Id);
        Assert.Contains(band.Name, title);
    }

    [Fact]
    public void BandRiderPrintStrategy_Render_ReturnsFragment()
    {
        var (bands, band) = Seed();
        var strat = new BandRiderPrintStrategy(bands);
        var frag = strat.Render(band.Id);
        Assert.NotNull(frag);
    }

    [Fact]
    public void BandRiderPrintStrategy_InvalidContext_Throws()
    {
        var strat = new BandRiderPrintStrategy(Seed().Item1);
        Assert.Throws<ArgumentException>(() => strat.GetTitle("wrong"));
        Assert.Throws<ArgumentException>(() => strat.Render("wrong"));
    }

    [Fact]
    public void StagePrintStrategy_KeyIsStage()
    {
        var (bands, _) = Seed();
        var strat = new StagePrintStrategy(bands);
        Assert.Equal("stage", strat.Key);
    }

    [Fact]
    public void StagePrintStrategy_Title_ContainsStageAndDate()
    {
        var (bands, _) = Seed();
        var ro = bands.RunningOrders[0];
        var strat = new StagePrintStrategy(bands);
        var title = strat.GetTitle(new StageContext(ro.Id, 1));
        Assert.Contains("Main", title);
        Assert.Contains("2024", title);
    }

    [Fact]
    public void StagePrintStrategy_Render_ReturnsFragment()
    {
        var (bands, _) = Seed();
        var ro = bands.RunningOrders[0];
        var strat = new StagePrintStrategy(bands);
        var frag = strat.Render(new StageContext(ro.Id, 1));
        Assert.NotNull(frag);
    }

    [Fact]
    public void StagePrintStrategy_InvalidContext_Throws()
    {
        var strat = new StagePrintStrategy(Seed().Item1);
        Assert.Throws<ArgumentException>(() => strat.GetTitle("wrong"));
    }

    [Fact]
    public void RolePrintStrategy_KeyIsRole()
    {
        var (bands, _) = Seed();
        var strat = new RolePrintStrategy(bands);
        Assert.Equal("role", strat.Key);
    }

    [Fact]
    public void RolePrintStrategy_Title_ContainsRoleAndDate()
    {
        var (bands, _) = Seed();
        var ro = bands.RunningOrders[0];
        var strat = new RolePrintStrategy(bands);
        var title = strat.GetTitle(new RoleContext(ro.Id, ContactRole.FOHEngineer));
        Assert.Contains("FOH engineer", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2024", title);
    }

    [Fact]
    public void RolePrintStrategy_Render_ReturnsFragment()
    {
        var (bands, _) = Seed();
        var ro = bands.RunningOrders[0];
        var strat = new RolePrintStrategy(bands);
        var frag = strat.Render(new RoleContext(ro.Id, ContactRole.FOHEngineer));
        Assert.NotNull(frag);
    }

    [Fact]
    public void RolePrintStrategy_InvalidContext_Throws()
    {
        var strat = new RolePrintStrategy(Seed().Item1);
        Assert.Throws<ArgumentException>(() => strat.GetTitle(Guid.NewGuid()));
    }

    [Fact]
    public void AllKeys_AreUniqueAndLowercase()
    {
        var (bands, _) = Seed();
        var keys = new[]
        {
            new BandRiderPrintStrategy(bands).Key,
            new StagePrintStrategy(bands).Key,
            new RolePrintStrategy(bands).Key,
        };
        Assert.Equal(3, keys.Distinct().Count());
        Assert.All(keys, k => Assert.True(k.All(char.IsAsciiLetterLower) && !k.Contains(' ')));
    }
}
