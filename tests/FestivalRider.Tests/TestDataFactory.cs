using FestivalRider.Models;

namespace FestivalRider.Tests;

public static class TestDataFactory
{
    public static Band FullBand(Guid? id = null)
    {
        var band = new Band
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Test Band",
            Notes = "Some notes",
            CreatedAt = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2024, 1, 2, 12, 0, 0, TimeSpan.Zero),
        };
        band.Contacts.Add(new Contact
        {
            Role = ContactRole.TourManager,
            Name = "Alice",
            Email = "alice@test.com",
            Phone = "+1-555-0001"
        });
        band.TravelParty.Members.Add(new Party
        {
            Type = PartyType.BandMember,
            Role = "Drums",
            Name = "Bob"
        });

        var t = band.Rider.Tech;
        t.Cables.Add(new Cable
        {
            Source = CablePoint.StageCenter,
            Target = CablePoint.SoundFoh,
            Type = CableType.RJ45,
            CategoryOrSpec = "Cat6",
            MinLengthMeters = 15m,
            Provider = CableProvider.Venue
        });
        t.Lighting.OwnConsoleModel = "MA Lighting dot2";
        t.Lighting.FloorMachines.Add(new LightingMachine { Name = "Wash", Count = 4 });
        t.Lighting.BackdropWidthMeters = 6m;
        t.Lighting.BackdropHeightMeters = 3m;
        t.Power.Amperage = PowerAmperage._63_A;
        t.Power.Phase = PowerPhase.ThreePhase;
        t.Foh.OwnConsoleModel = "Yamaha QL5";
        t.Foh.OutputProtocol = OutputProtocol.Aes;
        t.Foh.OutputLocation = OutputLocation.Foh;
        t.Foh.FootprintWidthMeters = 2.5m;
        t.Foh.FootprintLengthMeters = 1.5m;
        t.Monitors.SourceMode = MonitorSourceMode.OwnConsole;
        t.Monitors.OwnConsoleModel = "Avid S6L";
        t.Monitors.OwnConsoleLocation = MonitorTechLocation.OnStage;
        t.Monitors.Wedges.Add(new MonitorWedge { Where = "Drummer", DualLinked = true });
        t.Monitors.InEars.Add(new InEarMonitor { Where = "Singer", IsWireless = true, Provider = CableProvider.Venue, Model = "Sennheiser G4" });
        t.Stage.Risers.Add(new Riser { Where = "Center", WidthMeters = 3m, LengthMeters = 2m, HeightCm = 40 });
        t.Stage.WirelessMics.Add(new WirelessMic { Where = "Vocals", Count = 2, Provider = CableProvider.Brought });

        var h = band.Rider.Hospitality;
        h.DressingRoomNotes = "Clean towels";
        h.CateringNotes = "Vegetarian option";
        h.DietaryRestrictions = "No nuts";
        h.DrinksRequests.Add("Still water");
        h.TowelCount = 8;
        h.ParkingSpaces = 3;
        h.Accommodations = "Two hotel rooms";

        return band;
    }

    public static ShowData FullShow()
    {
        var show = new ShowData
        {
            Name = "Festival 2024",
            Address = "123 Main St",
            DateOfOpening = new DateOnly(2024, 6, 15),
            ShowDayCount = 3
        };
        show.Stages.Add(new Stage { Id = 1, Name = "Main" });
        show.Stages.Add(new Stage { Id = 2, Name = "Acoustic" });
        return show;
    }

    public static RunningOrder FullRunningOrder(ShowData show, Band band)
    {
        return new RunningOrder
        {
            Id = Guid.NewGuid(),
            ShowDayNumber = 1,
            Slots = new List<RunningOrderSlot>
            {
                new(band.Id, show.Stages[0].Id, new TimeOnly(18, 0), 60, 15, "Headliner"),
                new(band.Id, show.Stages[1].Id, new TimeOnly(14, 0), 30, 10, "Warmup"),
            }
        };
    }
}
