using FestivalRider.Models;
using FestivalRider.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace FestivalRider.PrintStrategies;

// Key "band". Context: Guid bandId. Renders band header, contacts, travel party, tech rider, hospitality.
public class BandRiderPrintStrategy : IPrintStrategy
{
    private readonly IBandService _bands;
    private readonly ILocalizationService _loc;

    public BandRiderPrintStrategy(IBandService bands, ILocalizationService loc)
    {
        _bands = bands;
        _loc = loc;
    }

    public string Key => "band";

    public string GetTitle(object context)
    {
        var band = ResolveBand(context);
        var showName = ActiveShow().Name;
        return string.IsNullOrWhiteSpace(showName)
            ? _loc.T("print.band.title", band.Name)
            : _loc.T("print.band.titleWithShow", showName, band.Name);
    }

    public RenderFragment Render(object context)
    {
        var band = ResolveBand(context);
        var show = ActiveShow();
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

    private ShowData ActiveShow() => _bands.FindShow(_bands.ActiveShowId) ?? new ShowData();

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

    private void BuildHeader(RenderTreeBuilder b, ref int seq, Band band, ShowData show)
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
        Field(b, ref seq, _loc.T("field.notes"), band.Notes);
        b.CloseElement();
    }

    private void BuildContacts(RenderTreeBuilder b, ref int seq, Band band)
    {
        if (band.Contacts.Count == 0) return;
        OpenSection(b, ref seq, _loc.T("section.contacts.title"));
        b.OpenElement(seq++, "table");
        b.AddAttribute(seq++, "class", "table table-sm");
        b.OpenElement(seq++, "thead");
        b.OpenElement(seq++, "tr");
        foreach (var h in new[] { _loc.T("field.role"), _loc.T("field.name"), _loc.T("field.email"), _loc.T("field.phone") })
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
            foreach (var v in new[] { _loc.T($"enum.ContactRole.{c.Role}"), c.Name, c.Email ?? string.Empty, c.Phone ?? string.Empty })
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

    private void BuildTravelParty(RenderTreeBuilder b, ref int seq, Band band)
    {
        var members = band.TravelParty.Members;
        if (members.Count == 0) return;
        OpenSection(b, ref seq, _loc.T("print.band.travelPartyHeading", members.Count));
        b.OpenElement(seq++, "table");
        b.AddAttribute(seq++, "class", "table table-sm");
        b.OpenElement(seq++, "thead");
        b.OpenElement(seq++, "tr");
        foreach (var h in new[] { _loc.T("field.type"), _loc.T("field.role"), _loc.T("field.name") })
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
            foreach (var v in new[] { _loc.T($"enum.PartyType.{p.Type}"), p.Role, p.Name })
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

    private void BuildTech(RenderTreeBuilder b, ref int seq, TechRider t)
    {
        OpenSection(b, ref seq, _loc.T("page.editor.tech.heading"));

        // Cabling
        if (t.Cables.Count > 0)
        {
            b.OpenElement(seq++, "h3");
            b.AddAttribute(seq++, "class", "h6");
            b.AddContent(seq++, _loc.T("section.cabling.title"));
            b.CloseElement();
            b.OpenElement(seq++, "table");
            b.AddAttribute(seq++, "class", "table table-sm");
            b.OpenElement(seq++, "thead");
            b.OpenElement(seq++, "tr");
            foreach (var h in new[] { _loc.T("field.cable.source"), _loc.T("field.cable.target"), _loc.T("field.type"), _loc.T("field.spec"), _loc.T("print.band.col.minM"), _loc.T("field.provider") })
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
                    _loc.T($"enum.CableProvider.{c.Provider}"),
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
        Field(b, ref seq, _loc.T("print.band.field.lightingConsole"), t.Lighting.OwnConsoleModel);
        if (t.Lighting.FloorMachines.Count > 0)
        {
            b.OpenElement(seq++, "div");
            b.OpenElement(seq++, "strong");
            b.AddContent(seq++, _loc.T("field.lighting.floorMachines") + ": ");
            b.CloseElement();
            b.AddContent(seq++, string.Join(", ", t.Lighting.FloorMachines.Select(m => $"{m.Count}× {m.Name}")));
            b.CloseElement();
        }
        if (t.Lighting.BackdropWidthMeters is { } w && t.Lighting.BackdropHeightMeters is { } h2)
            Field(b, ref seq, _loc.T("print.band.field.backdrop"), $"{w}m × {h2}m");

        // Power
        Field(b, ref seq, _loc.T("print.band.field.power"), $"{AmpLabel(t.Power.Amperage)} {PhaseLabel(t.Power.Phase)}");
        Field(b, ref seq, _loc.T("field.power.adapterNotes"), t.Power.AdapterNotes);

        // FOH
        Field(b, ref seq, _loc.T("print.band.field.fohConsole"), t.Foh.OwnConsoleModel);
        Field(b, ref seq, _loc.T("print.band.field.fohOutput"), $"{_loc.T($"enum.OutputProtocol.{t.Foh.OutputProtocol}")} @ {_loc.T($"enum.OutputLocation.{t.Foh.OutputLocation}")}");
        Field(b, ref seq, _loc.T("field.foh.outputNotes"), t.Foh.OutputNotes);
        Field(b, ref seq, _loc.T("field.foh.additionalHardware"), t.Foh.AdditionalHardware);
        if (t.Foh.StageToFohSendCount > 0)
            Field(b, ref seq, _loc.T("field.foh.stageToFohSends"), $"{t.Foh.StageToFohSendCount}{(t.Foh.StageToFohRoundTrip ? " (" + _loc.T("print.band.roundTrip") + ")" : "")}");
        if (t.Foh.FootprintWidthMeters is { } fw && t.Foh.FootprintLengthMeters is { } fl)
            Field(b, ref seq, _loc.T("print.band.field.fohFootprint"), $"{fw}m × {fl}m");
        Field(b, ref seq, _loc.T("print.band.field.fohNotes"), t.Foh.Notes);

        // Monitors
        Field(b, ref seq, _loc.T("print.band.field.monitorSource"), _loc.T($"enum.MonitorSourceMode.{t.Monitors.SourceMode}"));
        if (t.Monitors.SourceMode == MonitorSourceMode.OwnConsole)
        {
            Field(b, ref seq, _loc.T("print.band.field.monitorConsole"), t.Monitors.OwnConsoleModel);
            Field(b, ref seq, _loc.T("print.band.field.monitorLocation"), _loc.T($"enum.MonitorTechLocation.{t.Monitors.OwnConsoleLocation}"));
        }
        if (t.Monitors.Wedges.Count > 0)
        {
            b.OpenElement(seq++, "div");
            b.OpenElement(seq++, "strong");
            b.AddContent(seq++, _loc.T("field.monitors.wedges") + ": ");
            b.CloseElement();
            b.AddContent(seq++, string.Join("; ", t.Monitors.Wedges.Select(WedgeLabel)));
            b.CloseElement();
        }
        if (t.Monitors.InEars.Count > 0)
        {
            b.OpenElement(seq++, "div");
            b.OpenElement(seq++, "strong");
            b.AddContent(seq++, _loc.T("field.monitors.inEars") + ": ");
            b.CloseElement();
            b.AddContent(seq++, string.Join("; ", t.Monitors.InEars.Select(IemLabel)));
            b.CloseElement();
        }
        Field(b, ref seq, _loc.T("print.band.field.monitorNotes"), t.Monitors.Notes);

        // Stage
        if (t.Stage.Risers.Count > 0)
        {
            b.OpenElement(seq++, "div");
            b.OpenElement(seq++, "strong");
            b.AddContent(seq++, _loc.T("print.band.field.risers") + ": ");
            b.CloseElement();
            b.AddContent(seq++, string.Join("; ", t.Stage.Risers.Select(r => $"{r.Where} {r.WidthMeters}×{r.LengthMeters}m h{r.HeightCm}cm")));
            b.CloseElement();
        }
        if (t.Stage.OtherRisers.Count > 0)
        {
            b.OpenElement(seq++, "div");
            b.OpenElement(seq++, "strong");
            b.AddContent(seq++, _loc.T("print.band.field.otherRisers") + ": ");
            b.CloseElement();
            b.AddContent(seq++, string.Join("; ", t.Stage.OtherRisers.Select(r => $"{r.Where} {(r.Type == OtherRiserType.Custom ? r.Description ?? _loc.T("enum.OtherRiserType.Custom") : _loc.T($"enum.OtherRiserType.{r.Type}"))}")));
            b.CloseElement();
        }
        if (t.Stage.WirelessMics.Count > 0)
        {
            b.OpenElement(seq++, "div");
            b.OpenElement(seq++, "strong");
            b.AddContent(seq++, _loc.T("field.stage.wirelessMics") + ": ");
            b.CloseElement();
            b.AddContent(seq++, string.Join("; ", t.Stage.WirelessMics.Select(WirelessLabel)));
            b.CloseElement();
        }
        if (t.Stage.BringsOwnMics)
            Field(b, ref seq, _loc.T("print.band.field.mics"), _loc.T("print.band.field.bringsOwnMics"));
        Field(b, ref seq, _loc.T("print.band.field.stageNotes"), t.Stage.Notes);

        Field(b, ref seq, _loc.T("print.band.field.techNotes"), t.Notes);

        CloseSection(b);
    }

    private void BuildHospitality(RenderTreeBuilder b, ref int seq, HospitalityRider h)
    {
        OpenSection(b, ref seq, _loc.T("section.hospitality.title"));
        Field(b, ref seq, _loc.T("print.band.field.dressingRoom"), h.DressingRoomNotes);
        Field(b, ref seq, _loc.T("print.band.field.catering"), h.CateringNotes);
        if (h.DrinksRequests.Count > 0)
            Field(b, ref seq, _loc.T("print.band.field.drinks"), string.Join(", ", h.DrinksRequests));
        Field(b, ref seq, _loc.T("field.hospitality.dietaryRestrictions"), h.DietaryRestrictions);
        if (h.TowelCount > 0) Field(b, ref seq, _loc.T("print.band.field.towels"), h.TowelCount.ToString());
        if (h.ParkingSpaces > 0) Field(b, ref seq, _loc.T("field.hospitality.parkingSpaces"), h.ParkingSpaces.ToString());
        Field(b, ref seq, _loc.T("field.hospitality.accommodations"), h.Accommodations);
        CloseSection(b);
    }

    private string PointLabel(CablePoint p, string? other) =>
        p == CablePoint.Other && !string.IsNullOrWhiteSpace(other) ? other! : _loc.T($"enum.CablePoint.{p}");

    private string TypeLabel(CableType t, string? other) =>
        t == CableType.Other && !string.IsNullOrWhiteSpace(other) ? other! : _loc.T($"enum.CableType.{t}");

    private string AmpLabel(PowerAmperage a) => _loc.T($"enum.PowerAmperage.{a}");

    private string PhaseLabel(PowerPhase p) => _loc.T($"enum.PowerPhase.{p}");

    private string WedgeLabel(MonitorWedge w)
    {
        var flags = new List<string>();
        if (w.DualLinked) flags.Add(_loc.T("field.monitors.dual"));
        if (w.Stereo) flags.Add(_loc.T("field.monitors.stereo"));
        if (w.DrumFill) flags.Add(_loc.T("field.monitors.drumfill"));
        return flags.Count == 0 ? w.Where : $"{w.Where} ({string.Join(", ", flags)})";
    }

    private string IemLabel(InEarMonitor i)
    {
        var s = i.Where + (i.IsWireless ? " " + _loc.T("print.band.wireless") : " " + _loc.T("print.band.wired"));
        if (i.IsWireless && i.Provider == CableProvider.Brought)
        {
            if (!string.IsNullOrWhiteSpace(i.Model)) s += $" {i.Model}";
            if (!string.IsNullOrWhiteSpace(i.Frequency)) s += $" @ {i.Frequency}";
        }
        return s;
    }

    private string WirelessLabel(WirelessMic m)
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
