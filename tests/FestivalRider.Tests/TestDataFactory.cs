using System.IO.Compression;
using System.Text;
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
            MaxLengthMeters = 25m,
            Provider = CableProvider.Venue
        });
        t.Lighting.OwnConsoleModel = "MA Lighting dot2";
        t.Lighting.FloorMachines.Add(new LightingMachine { Name = "Wash", Location = "Stage left", Count = 4 });
        t.Lighting.BackdropWidthMeters = 6m;
        t.Lighting.BackdropHeightMeters = 3m;
        t.Power.Amperage = PowerAmperage._63_A;
        t.Power.Phase = PowerPhase.ThreePhase;
        t.Foh.OwnConsoleModel = "Yamaha QL5";
        t.Foh.OutputProtocol = OutputProtocol.Aes;
        t.Foh.OutputLocation = OutputLocation.Foh;
        t.Foh.StageToFohSendCount = 32;
        t.Foh.StageToFohRoundTripCount = 2;
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
        show.Bands = new List<Band>();
        show.RunningOrders = new List<RunningOrder>();
        return show;
    }

    public static ShowData ShowWithNoStages()
    {
        var show = new ShowData
        {
            Name = "No Stages Show",
            Address = "Nowhere",
            DateOfOpening = new DateOnly(2024, 6, 15),
            ShowDayCount = 1,
        };
        show.Bands = new List<Band>();
        show.RunningOrders = new List<RunningOrder>();
        return show;
    }

    /// <summary>
    /// Hand-rolled v1 JSON payload (per plan 001's shape) for migration tests.
    /// Includes a band with `genre`, `rider.tech.inputs`, `backlineItems`, plus a running order.
    /// </summary>
    public static string BuildV1JsonPayload() => """
        {
          "schemaVersion": 1,
          "bands": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "name": "Alpha",
              "genre": "Rock",
              "rider": {
                "tech": {
                  "inputs": [
                    { "channel": 1, "source": "Kick" },
                    { "channel": 2, "source": "Snare" }
                  ],
                  "backlineItems": [ { "name": "Amp" } ]
                }
              }
            }
          ],
          "runningOrders": [
            { "id": "33333333-3333-3333-3333-333333333333", "showDayNumber": 1, "slots": [] }
          ]
        }
        """;

    /// <summary>
    /// Hand-rolls a v2 .zip bundle in plan-003 wire format (`schemaVersion: 2`, single
    /// `show` field on the manifest, running-order CSV without a `ShowId` column) so
    /// migration integration tests can exercise the v2 → v3 path without depending on a
    /// long-deleted `BundleService` build.
    /// </summary>
    public static byte[] BuildV2BundleZip()
    {
        var bandId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var orderId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var showCsv = "Section,Key,Value,Index,Notes\nShow,Name,V2 Festival,,\nShow,ShowDayCount,1,,\n";
        var bandCsv = $"Section,Key,Value,Index,Notes\nBand,Id,{bandId},,\nBand,Name,Alpha,,\n";
        var roCsv =
            "Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes\n" +
            "Main,18:00,Alpha,60,15,Headliner\n";

        var manifest = "{" +
            "\"format\":\"festivalrider-bundle\"," +
            "\"schemaVersion\":2," +
            "\"exportedAt\":\"2024-06-15T00:00:00+00:00\"," +
            "\"show\":\"show.csv\"," +
            $"\"bands\":[\"bands/{bandId}.csv\"]," +
            $"\"runningOrders\":[\"running-orders/{orderId}.csv\"]" +
            "}";

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "show.csv", showCsv);
            WriteEntry(zip, $"bands/{bandId}.csv", bandCsv);
            WriteEntry(zip, $"running-orders/{orderId}.csv", roCsv);
            WriteEntry(zip, "manifest.json", manifest);
        }
        return ms.ToArray();

        static void WriteEntry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name);
            using var s = entry.Open();
            var bytes = new UTF8Encoding(false).GetBytes(content);
            s.Write(bytes, 0, bytes.Length);
        }
    }

    public static RunningOrder FullRunningOrder(ShowData show, Band band)
    {
        var ro = new RunningOrder
        {
            Id = Guid.NewGuid(),
            ShowId = show.Id,
            ShowDayNumber = 1,
            Slots = new List<RunningOrderSlot>
            {
                Slot(band.Id, show.Stages[0].Id, show, 1, new TimeOnly(18, 0), 60, "Headliner"),
                Slot(band.Id, show.Stages[1].Id, show, 1, new TimeOnly(14, 0), 30, "Warmup"),
            }
        };
        show.RunningOrders.Add(ro);
        return ro;
    }

    public static RunningOrderSlot Slot(Guid bandId, int stageId, ShowData show, int showDayNumber,
        TimeOnly onStage, int setLengthMinutes, string? notes = null)
    {
        var baseDate = show.DateOfOpening == default
            ? DateTime.Today
            : show.DateOfOpening.AddDays(Math.Max(0, showDayNumber - 1)).ToDateTime(TimeOnly.MinValue);
        return new RunningOrderSlot
        {
            BandId = bandId,
            StageId = stageId,
            OnStageTime = baseDate.Add(onStage.ToTimeSpan()),
            IsOnStagePinned = true,
            SetLengthMinutes = setLengthMinutes,
            Notes = notes,
        };
    }
}
