using FestivalRider.Models;
using FestivalRider.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace FestivalRider.PrintStrategies;

// Key "band". Context: Guid bandId. Renders band header, contacts, travel party, tech rider, hospitality.
public class BandRiderPrintStrategy : IPrintStrategy
{
    private readonly IBandService _bands;

    public BandRiderPrintStrategy(IBandService bands)
    {
        _bands = bands;
    }

    public string Key => "band";

    public string GetTitle(object context)
    {
        var band = ResolveBand(context);
        var showName = _bands.Snapshot().ShowData.Name;
        return string.IsNullOrWhiteSpace(showName)
            ? $"{band.Name} — rider"
            : $"{showName} — {band.Name} rider";
    }

    public RenderFragment Render(object context)
    {
        var band = ResolveBand(context);
        var show = _bands.Snapshot().ShowData;
        return builder =>
        {
            var seq = 0;
            BuildHeader(builder, ref seq, band, show);
            BuildContacts(builder, ref seq, band);
            BuildTravelParty(builder, ref seq, band);
            BuildTech(builder, ref seq, band.Rider.Tech);
            BuildHospitality(builder, ref seq, band.Rider.Hospitality);
        };
    }

    private Band ResolveBand(object context)
    {
        if (context is not Guid id)
            throw new ArgumentException($"BandRiderPrintStrategy expects Guid context, got {context?.GetType().Name ?? "null"}.", nameof(context));
        var band = _bands.FindBand(id) ?? throw new InvalidOperationException($"Band {id} not found.");
        return band;
    }

    private static void OpenSection(RenderTreeBuilder b, ref int seq, string title)
    {
        b.OpenElement(seq++, "section");
        b.AddAttribute(seq++, "class", "print-section mb-3");
        b.OpenElement(seq++, "h2");
        b.AddAttribute(seq++, "class", "h5");
        b.AddContent(seq++, title);
        b.CloseElement();
    }

    private static void CloseSection(RenderTreeBuilder b) => b.CloseElement();

    private static void Field(RenderTreeBuilder b, ref int seq, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        b.OpenElement(seq++, "div");
        b.OpenElement(seq++, "strong");
        b.AddContent(seq++, label + ": ");
        b.CloseElement();
        b.AddContent(seq++, value);
        b.CloseElement();
    }

    private static void BuildHeader(RenderTreeBuilder b, ref int seq, Band band, ShowData show)
    {
        b.OpenElement(seq++, "header");
        b.AddAttribute(seq++, "class", "print-section mb-3");
        b.OpenElement(seq++, "h1");
        b.AddAttribute(seq++, "class", "h3");
        b.AddContent(seq++, band.Name);
        b.CloseElement();
        if (!string.IsNullOrWhiteSpace(show.Name))
        {
            b.OpenElement(seq++, "div");
            b.AddAttribute(seq++, "class", "text-muted");
            b.AddContent(seq++, show.Name);
            if (show.DateOfOpening != default)
            {
                b.AddContent(seq++, " · ");
                b.AddContent(seq++, show.DateOfOpening.ToString("yyyy-MM-dd"));
            }
            b.CloseElement();
        }
        Field(b, ref seq, "Notes", band.Notes);
        b.CloseElement();
    }

    private static void BuildContacts(RenderTreeBuilder b, ref int seq, Band band)
    {
        if (band.Contacts.Count == 0) return;
        OpenSection(b, ref seq, "Contacts");
        b.OpenElement(seq++, "table");
        b.AddAttribute(seq++, "class", "table table-sm");
        b.OpenElement(seq++, "thead");
        b.OpenElement(seq++, "tr");
        foreach (var h in new[] { "Role", "Name", "Email", "Phone" })
        {
            b.OpenElement(seq++, "th");
            b.AddContent(seq++, h);
            b.CloseElement();
        }
        b.CloseElement();
        b.CloseElement();
        b.OpenElement(seq++, "tbody");
        foreach (var c in band.Contacts)
        {
            b.OpenElement(seq++, "tr");
            foreach (var v in new[] { c.Role.ToString(), c.Name, c.Email ?? string.Empty, c.Phone ?? string.Empty })
            {
                b.OpenElement(seq++, "td");
                b.AddContent(seq++, v);
                b.CloseElement();
            }
            b.CloseElement();
        }
        b.CloseElement();
        b.CloseElement();
        CloseSection(b);
    }

    private static void BuildTravelParty(RenderTreeBuilder b, ref int seq, Band band)
    {
        var members = band.TravelParty.Members;
        if (members.Count == 0) return;
        OpenSection(b, ref seq, $"Travel party ({members.Count})");
        b.OpenElement(seq++, "table");
        b.AddAttribute(seq++, "class", "table table-sm");
        b.OpenElement(seq++, "thead");
        b.OpenElement(seq++, "tr");
        foreach (var h in new[] { "Type", "Role", "Name" })
        {
            b.OpenElement(seq++, "th");
            b.AddContent(seq++, h);
            b.CloseElement();
        }
        b.CloseElement();
        b.CloseElement();
        b.OpenElement(seq++, "tbody");
        foreach (var p in members)
        {
            b.OpenElement(seq++, "tr");
            foreach (var v in new[] { p.Type.ToString(), p.Role, p.Name })
            {
                b.OpenElement(seq++, "td");
                b.AddContent(seq++, v);
                b.CloseElement();
            }
            b.CloseElement();
        }
        b.CloseElement();
        b.CloseElement();
        CloseSection(b);
    }

    private static void BuildTech(RenderTreeBuilder b, ref int seq, TechRider t)
    {
        OpenSection(b, ref seq, "Tech rider");

        // Cabling
        if (t.Cables.Count > 0)
        {
            b.OpenElement(seq++, "h3");
            b.AddAttribute(seq++, "class", "h6");
            b.AddContent(seq++, "Cabling");
            b.CloseElement();
            b.OpenElement(seq++, "table");
            b.AddAttribute(seq++, "class", "table table-sm");
            b.OpenElement(seq++, "thead");
            b.OpenElement(seq++, "tr");
            foreach (var h in new[] { "Source", "Target", "Type", "Spec", "Min m", "Provider" })
            {
                b.OpenElement(seq++, "th");
                b.AddContent(seq++, h);
                b.CloseElement();
            }
            b.CloseElement();
            b.CloseElement();
            b.OpenElement(seq++, "tbody");
            foreach (var c in t.Cables)
            {
                b.OpenElement(seq++, "tr");
                foreach (var v in new[] {
                    PointLabel(c.Source, c.SourceOther),
                    PointLabel(c.Target, c.TargetOther),
                    TypeLabel(c.Type, c.TypeOther),
                    c.CategoryOrSpec ?? string.Empty,
                    c.MinLengthMeters?.ToString() ?? string.Empty,
                    c.Provider.ToString(),
                })
                {
                    b.OpenElement(seq++, "td");
                    b.AddContent(seq++, v);
                    b.CloseElement();
                }
                b.CloseElement();
            }
            b.CloseElement();
            b.CloseElement();
        }

        // Lighting
        Field(b, ref seq, "Lighting console", t.Lighting.OwnConsoleModel);
        if (t.Lighting.FloorMachines.Count > 0)
        {
            b.OpenElement(seq++, "div");
            b.OpenElement(seq++, "strong");
            b.AddContent(seq++, "Floor machines: ");
            b.CloseElement();
            b.AddContent(seq++, string.Join(", ", t.Lighting.FloorMachines.Select(m => $"{m.Count}× {m.Name}")));
            b.CloseElement();
        }
        if (t.Lighting.BackdropWidthMeters is { } w && t.Lighting.BackdropHeightMeters is { } h2)
            Field(b, ref seq, "Backdrop", $"{w}m × {h2}m");

        // Power
        Field(b, ref seq, "Power", $"{AmpLabel(t.Power.Amperage)} {PhaseLabel(t.Power.Phase)}");
        Field(b, ref seq, "Power adapter notes", t.Power.AdapterNotes);

        // FOH
        Field(b, ref seq, "FOH console", t.Foh.OwnConsoleModel);
        Field(b, ref seq, "FOH output", $"{t.Foh.OutputProtocol} @ {t.Foh.OutputLocation}");
        Field(b, ref seq, "FOH output notes", t.Foh.OutputNotes);
        Field(b, ref seq, "Additional hardware", t.Foh.AdditionalHardware);
        if (t.Foh.StageToFohSendCount > 0)
            Field(b, ref seq, "Stage→FOH sends", $"{t.Foh.StageToFohSendCount}{(t.Foh.StageToFohRoundTrip ? " (round trip)" : "")}");
        if (t.Foh.FootprintWidthMeters is { } fw && t.Foh.FootprintLengthMeters is { } fl)
            Field(b, ref seq, "FOH footprint", $"{fw}m × {fl}m");
        Field(b, ref seq, "FOH notes", t.Foh.Notes);

        // Monitors
        Field(b, ref seq, "Monitor source", t.Monitors.SourceMode.ToString());
        if (t.Monitors.SourceMode == MonitorSourceMode.OwnConsole)
        {
            Field(b, ref seq, "Monitor console", t.Monitors.OwnConsoleModel);
            Field(b, ref seq, "Monitor location", t.Monitors.OwnConsoleLocation.ToString());
        }
        if (t.Monitors.Wedges.Count > 0)
        {
            b.OpenElement(seq++, "div");
            b.OpenElement(seq++, "strong");
            b.AddContent(seq++, "Wedges: ");
            b.CloseElement();
            b.AddContent(seq++, string.Join("; ", t.Monitors.Wedges.Select(WedgeLabel)));
            b.CloseElement();
        }
        if (t.Monitors.InEars.Count > 0)
        {
            b.OpenElement(seq++, "div");
            b.OpenElement(seq++, "strong");
            b.AddContent(seq++, "In-ears: ");
            b.CloseElement();
            b.AddContent(seq++, string.Join("; ", t.Monitors.InEars.Select(IemLabel)));
            b.CloseElement();
        }
        Field(b, ref seq, "Monitor notes", t.Monitors.Notes);

        // Stage
        if (t.Stage.Risers.Count > 0)
        {
            b.OpenElement(seq++, "div");
            b.OpenElement(seq++, "strong");
            b.AddContent(seq++, "Risers: ");
            b.CloseElement();
            b.AddContent(seq++, string.Join("; ", t.Stage.Risers.Select(r => $"{r.Where} {r.WidthMeters}×{r.LengthMeters}m h{r.HeightCm}cm")));
            b.CloseElement();
        }
        if (t.Stage.OtherRisers.Count > 0)
        {
            b.OpenElement(seq++, "div");
            b.OpenElement(seq++, "strong");
            b.AddContent(seq++, "Other risers: ");
            b.CloseElement();
            b.AddContent(seq++, string.Join("; ", t.Stage.OtherRisers.Select(r => $"{r.Where} {(r.Type == OtherRiserType.Custom ? r.Description ?? "Custom" : r.Type.ToString())}")));
            b.CloseElement();
        }
        if (t.Stage.WirelessMics.Count > 0)
        {
            b.OpenElement(seq++, "div");
            b.OpenElement(seq++, "strong");
            b.AddContent(seq++, "Wireless mics: ");
            b.CloseElement();
            b.AddContent(seq++, string.Join("; ", t.Stage.WirelessMics.Select(WirelessLabel)));
            b.CloseElement();
        }
        if (t.Stage.BringsOwnMics)
            Field(b, ref seq, "Mics", "Brings own mics");
        Field(b, ref seq, "Stage notes", t.Stage.Notes);

        Field(b, ref seq, "Tech notes", t.Notes);

        CloseSection(b);
    }

    private static void BuildHospitality(RenderTreeBuilder b, ref int seq, HospitalityRider h)
    {
        OpenSection(b, ref seq, "Hospitality");
        Field(b, ref seq, "Dressing room", h.DressingRoomNotes);
        Field(b, ref seq, "Catering", h.CateringNotes);
        if (h.DrinksRequests.Count > 0)
            Field(b, ref seq, "Drinks", string.Join(", ", h.DrinksRequests));
        Field(b, ref seq, "Dietary restrictions", h.DietaryRestrictions);
        if (h.TowelCount > 0) Field(b, ref seq, "Towels", h.TowelCount.ToString());
        if (h.ParkingSpaces > 0) Field(b, ref seq, "Parking spaces", h.ParkingSpaces.ToString());
        Field(b, ref seq, "Accommodations", h.Accommodations);
        CloseSection(b);
    }

    private static string PointLabel(CablePoint p, string? other) =>
        p == CablePoint.Other && !string.IsNullOrWhiteSpace(other) ? other! : p.ToString();

    private static string TypeLabel(CableType t, string? other) =>
        t == CableType.Other && !string.IsNullOrWhiteSpace(other) ? other! : t.ToString();

    private static string AmpLabel(PowerAmperage a) => a switch
    {
        PowerAmperage._16_A => "16A",
        PowerAmperage._32_A => "32A",
        PowerAmperage._63_A => "63A",
        _ => a.ToString(),
    };

    private static string PhaseLabel(PowerPhase p) => p == PowerPhase.ThreePhase ? "3-phase" : "single-phase";

    private static string WedgeLabel(MonitorWedge w)
    {
        var flags = new List<string>();
        if (w.DualLinked) flags.Add("dual");
        if (w.Stereo) flags.Add("stereo");
        if (w.DrumFill) flags.Add("drum fill");
        return flags.Count == 0 ? w.Where : $"{w.Where} ({string.Join(", ", flags)})";
    }

    private static string IemLabel(InEarMonitor i)
    {
        var s = i.Where + (i.IsWireless ? " wireless" : " wired");
        if (i.IsWireless && i.Provider == CableProvider.Brought)
        {
            if (!string.IsNullOrWhiteSpace(i.Model)) s += $" {i.Model}";
            if (!string.IsNullOrWhiteSpace(i.Frequency)) s += $" @ {i.Frequency}";
        }
        return s;
    }

    private static string WirelessLabel(WirelessMic m)
    {
        var s = $"{m.Count}× {m.Where}";
        if (m.Provider == CableProvider.Brought)
        {
            if (!string.IsNullOrWhiteSpace(m.Model)) s += $" {m.Model}";
            if (!string.IsNullOrWhiteSpace(m.Frequency)) s += $" @ {m.Frequency}";
        }
        return s;
    }
}
