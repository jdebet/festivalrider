using System.Globalization;
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
        var show = new ShowData
        {
            Name = "Fest 2024",
            DateOfOpening = new DateOnly(2024, 6, 15),
            ShowDayCount = 2,
            Stages = { new Stage { Id = 1, Name = "Main" } }
        };
        var state = new AppState
        {
            Shows = new List<ShowData> { show },
            ActiveShowId = show.Id,
        };
        var bands = new BandService(NullLogger<BandService>.Instance);
        bands.ReplaceState(state);
        var band = TestDataFactory.FullBand();
        bands.AddBand(band);
        bands.AddRunningOrder(new RunningOrder
        {
            Id = Guid.NewGuid(),
            ShowId = show.Id,
            ShowDayNumber = 1,
            Slots = { new(band.Id, 1, new TimeOnly(20, 0), 60, 15, "Headliner") }
        });
        return (bands, band);
    }

    [Fact]
    public void BandRiderPrintStrategy_KeyIsBand()
    {
        var (bands, band) = Seed();
        var strat = new BandRiderPrintStrategy(bands, FakeLocalizationService.Instance);
        Assert.Equal("band", strat.Key);
    }

    [Fact]
    public void BandRiderPrintStrategy_Title_ContainsBandName()
    {
        var (bands, band) = Seed();
        var strat = new BandRiderPrintStrategy(bands, FakeLocalizationService.Instance);
        var title = strat.GetTitle(band.Id);
        Assert.Contains(band.Name, title);
    }

    [Fact]
    public void BandRiderPrintStrategy_Render_ReturnsFragment()
    {
        var (bands, band) = Seed();
        var strat = new BandRiderPrintStrategy(bands, FakeLocalizationService.Instance);
        var frag = strat.Render(band.Id);
        Assert.NotNull(frag);
    }

    [Fact]
    public void BandRiderPrintStrategy_InvalidContext_Throws()
    {
        var strat = new BandRiderPrintStrategy(Seed().Item1, FakeLocalizationService.Instance);
        Assert.Throws<ArgumentException>(() => strat.GetTitle("wrong"));
        Assert.Throws<ArgumentException>(() => strat.Render("wrong"));
    }

    [Fact]
    public void StagePrintStrategy_KeyIsStage()
    {
        var (bands, _) = Seed();
        var strat = new StagePrintStrategy(bands, FakeLocalizationService.Instance);
        Assert.Equal("stage", strat.Key);
    }

    [Fact]
    public void StagePrintStrategy_Title_ContainsStageAndDate()
    {
        var (bands, _) = Seed();
        var ro = bands.RunningOrders[0];
        var strat = new StagePrintStrategy(bands, FakeLocalizationService.Instance);
        var title = strat.GetTitle(new StageContext(ro.Id, 1));
        Assert.Contains("Main", title);
        Assert.Contains("2024", title);
    }

    [Fact]
    public void StagePrintStrategy_Render_ReturnsFragment()
    {
        var (bands, _) = Seed();
        var ro = bands.RunningOrders[0];
        var strat = new StagePrintStrategy(bands, FakeLocalizationService.Instance);
        var frag = strat.Render(new StageContext(ro.Id, 1));
        Assert.NotNull(frag);
    }

    [Fact]
    public void StagePrintStrategy_InvalidContext_Throws()
    {
        var strat = new StagePrintStrategy(Seed().Item1, FakeLocalizationService.Instance);
        Assert.Throws<ArgumentException>(() => strat.GetTitle("wrong"));
    }

    [Fact]
    public void RolePrintStrategy_KeyIsRole()
    {
        var (bands, _) = Seed();
        var strat = new RolePrintStrategy(bands, FakeLocalizationService.Instance);
        Assert.Equal("role", strat.Key);
    }

    [Fact]
    public void RolePrintStrategy_Title_ContainsRoleAndDate()
    {
        var (bands, _) = Seed();
        var ro = bands.RunningOrders[0];
        var strat = new RolePrintStrategy(bands, FakeLocalizationService.Instance);
        var title = strat.GetTitle(new RoleContext(ro.Id, ContactRole.FOHEngineer));
        Assert.Contains("FOH engineer", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2024", title);
    }

    [Fact]
    public void RolePrintStrategy_Render_ReturnsFragment()
    {
        var (bands, _) = Seed();
        var ro = bands.RunningOrders[0];
        var strat = new RolePrintStrategy(bands, FakeLocalizationService.Instance);
        var frag = strat.Render(new RoleContext(ro.Id, ContactRole.FOHEngineer));
        Assert.NotNull(frag);
    }

    [Fact]
    public void RolePrintStrategy_InvalidContext_Throws()
    {
        var strat = new RolePrintStrategy(Seed().Item1, FakeLocalizationService.Instance);
        Assert.Throws<ArgumentException>(() => strat.GetTitle(Guid.NewGuid()));
    }

    [Fact]
    public void AllKeys_AreUniqueAndLowercase()
    {
        var (bands, _) = Seed();
        var keys = new[]
        {
            new BandRiderPrintStrategy(bands, FakeLocalizationService.Instance).Key,
            new StagePrintStrategy(bands, FakeLocalizationService.Instance).Key,
            new RolePrintStrategy(bands, FakeLocalizationService.Instance).Key,
        };
        Assert.Equal(3, keys.Distinct().Count());
        Assert.All(keys, k => Assert.True(k.All(char.IsAsciiLetterLower) && !k.Contains(' ')));
    }

    [Fact]
    public void StagePrintStrategy_Title_UsesLocalizationCultureForDate()
    {
        var (bands, _) = Seed();
        var ro = bands.RunningOrders[0];

        var enLoc = FakeLocalizationService.Instance;
        var frLoc = new FakeLocalizationService(CultureInfo.GetCultureInfo("fr-FR"),
            new Dictionary<string, string>
            {
                ["print.day"] = "Jour {0}",
                ["enum.ContactRole.FOHEngineer"] = "Ingénieur façade",
                ["print.band.title"] = "{0} \u2014 fiche technique",
                ["print.band.titleWithShow"] = "{0} \u2014 fiche technique {1}",
            });

        var enTitle = new StagePrintStrategy(bands, enLoc).GetTitle(new StageContext(ro.Id, 1));
        var frTitle = new StagePrintStrategy(bands, frLoc).GetTitle(new StageContext(ro.Id, 1));

        Assert.Contains("2024", enTitle);
        Assert.Contains("2024", frTitle);
        Assert.NotEqual(enTitle, frTitle);
    }

    [Fact]
    public void RolePrintStrategy_Title_UsesLocalizationCultureForDate()
    {
        var (bands, _) = Seed();
        var ro = bands.RunningOrders[0];

        var frLoc = new FakeLocalizationService(CultureInfo.GetCultureInfo("fr-FR"),
            new Dictionary<string, string>
            {
                ["print.day"] = "Jour {0}",
                ["enum.ContactRole.FOHEngineer"] = "Ingénieur façade",
            });

        var enTitle = new RolePrintStrategy(bands, FakeLocalizationService.Instance)
            .GetTitle(new RoleContext(ro.Id, ContactRole.FOHEngineer));
        var frTitle = new RolePrintStrategy(bands, frLoc)
            .GetTitle(new RoleContext(ro.Id, ContactRole.FOHEngineer));

        Assert.Contains("2024", frTitle);
        Assert.Contains("Ingénieur façade", frTitle);
        Assert.NotEqual(enTitle, frTitle);
    }
}
