using FestivalRider.Models;
using FestivalRider.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FestivalRider.Tests;

public sealed class BandServiceTests
{
    private static BandService Create() => new(NullLogger<BandService>.Instance);

    [Fact]
    public void AddBand_InsertsAndRaisesOnChange()
    {
        var svc = Create();
        var fired = false;
        svc.OnChange += () => fired = true;

        var band = new Band { Id = Guid.NewGuid(), Name = "Test Band" };
        svc.AddBand(band);

        Assert.Single(svc.Bands);
        Assert.True(fired);
        Assert.NotEqual(default, band.CreatedAt);
        Assert.NotEqual(default, band.UpdatedAt);
    }

    [Fact]
    public void AddBand_ThrowsOnDuplicateId()
    {
        var svc = Create();
        var id = Guid.NewGuid();
        svc.AddBand(new Band { Id = id, Name = "A" });
        Assert.Throws<InvalidOperationException>(() => svc.AddBand(new Band { Id = id, Name = "B" }));
    }

    [Fact]
    public void AddBand_ThrowsOnNull()
    {
        var svc = Create();
        Assert.Throws<ArgumentNullException>(() => svc.AddBand(null!));
    }

    [Fact]
    public void UpdateBand_UpdatesAndRaisesOnChange()
    {
        var svc = Create();
        var id = Guid.NewGuid();
        svc.AddBand(new Band { Id = id, Name = "Old" });

        var fired = false;
        svc.OnChange += () => fired = true;

        var before = svc.FindBand(id)!.UpdatedAt;
        svc.UpdateBand(new Band { Id = id, Name = "New" });

        Assert.True(fired);
        Assert.Equal("New", svc.FindBand(id)!.Name);
        Assert.True(svc.FindBand(id)!.UpdatedAt > before);
    }

    [Fact]
    public void UpdateBand_ThrowsOnMissingId()
    {
        var svc = Create();
        Assert.Throws<InvalidOperationException>(() => svc.UpdateBand(new Band { Id = Guid.NewGuid(), Name = "X" }));
    }

    [Fact]
    public void DeleteBand_RemovesBandAndSlots()
    {
        var svc = Create();
        var id = Guid.NewGuid();
        svc.AddBand(new Band { Id = id, Name = "A" });
        svc.AddRunningOrder(new RunningOrder
        {
            Id = Guid.NewGuid(),
            ShowDayNumber = 1,
            Slots = new List<RunningOrderSlot> { new(id, 1, TimeOnly.Parse("12:00"), 30, 0, null) }
        });

        var fired = false;
        svc.OnChange += () => fired = true;

        svc.DeleteBand(id);

        Assert.True(fired);
        Assert.Empty(svc.Bands);
        Assert.Empty(svc.RunningOrders[0].Slots);
    }

    [Fact]
    public void AddRunningOrder_InsertsAndRaisesOnChange()
    {
        var svc = Create();
        var fired = false;
        svc.OnChange += () => fired = true;

        svc.AddRunningOrder(new RunningOrder { Id = Guid.NewGuid(), ShowDayNumber = 1 });
        Assert.Single(svc.RunningOrders);
        Assert.True(fired);
    }

    [Fact]
    public void AddRunningOrder_ThrowsOnDuplicateId()
    {
        var svc = Create();
        var id = Guid.NewGuid();
        svc.AddRunningOrder(new RunningOrder { Id = id, ShowDayNumber = 1 });
        Assert.Throws<InvalidOperationException>(() => svc.AddRunningOrder(new RunningOrder { Id = id, ShowDayNumber = 2 }));
    }

    [Fact]
    public void DeleteRunningOrder_Removes()
    {
        var svc = Create();
        var id = Guid.NewGuid();
        svc.AddRunningOrder(new RunningOrder { Id = id, ShowDayNumber = 1 });
        svc.DeleteRunningOrder(id);
        Assert.Empty(svc.RunningOrders);
    }

    [Fact]
    public void AddStage_ReturnsId()
    {
        var svc = Create();
        var fired = false;
        svc.OnChange += () => fired = true;

        var id = svc.AddStage("Main");
        Assert.Equal(1, id);
        Assert.True(fired);
        Assert.Equal("Main", svc.FindStage(id)!.Name);
    }

    [Fact]
    public void UpdateStage_ChangesName()
    {
        var svc = Create();
        var id = svc.AddStage("Main");
        svc.UpdateStage(new Stage { Id = id, Name = "Second" });
        Assert.Equal("Second", svc.FindStage(id)!.Name);
    }

    [Fact]
    public void DeleteStage_Removes()
    {
        var svc = Create();
        var id = svc.AddStage("Main");
        svc.DeleteStage(id);
        Assert.Null(svc.FindStage(id));
    }

    [Fact]
    public void ReplaceState_OverwritesEverything()
    {
        var svc = Create();
        svc.AddBand(new Band { Id = Guid.NewGuid(), Name = "A" });
        svc.AddStage("Main");

        var replacement = new AppState();
        replacement.Shows[0].Bands.Add(new Band { Id = Guid.NewGuid(), Name = "B" });
        replacement.Shows[0].Stages.Add(new Stage { Id = 5, Name = "Big" });

        svc.ReplaceState(replacement);

        Assert.Single(svc.Bands);
        Assert.Equal("B", svc.Bands[0].Name);
        Assert.Equal("Big", svc.FindStage(5)!.Name);
    }

    [Fact]
    public void Snapshot_ReturnsSameReference()
    {
        var svc = Create();
        var snap = svc.Snapshot();
        Assert.Empty(snap.Shows[0].Bands);
        svc.AddBand(new Band { Id = Guid.NewGuid(), Name = "A" });
        Assert.Single(snap.Shows[0].Bands); // same reference
    }
}
