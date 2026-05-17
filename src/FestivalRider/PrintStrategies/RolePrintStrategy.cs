using FestivalRider.Models;
using FestivalRider.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace FestivalRider.PrintStrategies;

// Key "role". Context: RoleContext(runningOrderId, role). Per-role slice across the running order:
// for each band scheduled that day, shows the role contact and a role-tailored tech summary.
public class RolePrintStrategy : IPrintStrategy
{
    private readonly IBandService _bands;
    private readonly ILocalizationService _loc;

    public RolePrintStrategy(IBandService bands, ILocalizationService loc)
    {
        _bands = bands;
        _loc = loc;
    }

    public string Key => "role";

    public string GetTitle(object context)
    {
        var (order, role, _) = Resolve(context);
        var show = _bands.FindShow(order.ShowId) ?? new ShowData();
        var date = show.DateOfOpening == default
            ? _loc.T("print.day", order.ShowDayNumber)
            : show.DateOfOpening.AddDays(order.ShowDayNumber - 1).ToString("ddd dd MMM yyyy", _loc.Culture);
        var roleLabel = _loc.T($"enum.ContactRole.{role}");
        return string.IsNullOrWhiteSpace(show.Name)
            ? $"{roleLabel} — {date}"
            : $"{show.Name} — {roleLabel} — {date}";
    }

    public RenderFragment Render(object context)
    {
        var (order, role, slots) = Resolve(context);
        var show = _bands.FindShow(order.ShowId) ?? new ShowData();
        return builder =>
        {
            var seq = 0;
            BuildHeader(builder, ref seq, role, order, show);
            if (slots.Count == 0)
            {
                builder.OpenElement(seq++, "p");
                builder.AddContent(seq++, _loc.T("print.role.noSlots"));
                builder.CloseElement();
                return;
            }
            foreach (var s in slots)
            {
                var band = _bands.FindBand(order.ShowId, s.BandId);
                if (band is null) continue;
                BuildBandBlock(builder, ref seq, band, s, role, order.ShowId);
            }
        };
    }

    private (RunningOrder order, ContactRole role, IReadOnlyList<RunningOrderSlot> slots) Resolve(object context)
    {
        if (context is not RoleContext ctx)
            throw new ArgumentException($"RolePrintStrategy expects RoleContext, got {context?.GetType().Name ?? "null"}.", nameof(context));
        var order = _bands.FindRunningOrder(ctx.RunningOrderId)
            ?? throw new InvalidOperationException($"Running order {ctx.RunningOrderId} not found.");
        var slots = order.Slots.OrderBy(s => s.OnStageTime ?? DateTime.MaxValue).ToList();
        return (order, ctx.Role, slots);
    }

    private void BuildHeader(RenderTreeBuilder b, ref int seq, ContactRole role, RunningOrder order, ShowData show)
    {
        b.OpenElement(seq++, "header");
        b.AddAttribute(seq++, "class", "print-section mb-3");
        b.OpenElement(seq++, "h1");
        b.AddAttribute(seq++, "class", "h3");
        b.AddContent(seq++, _loc.T($"enum.ContactRole.{role}"));
        b.CloseElement();
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "text-muted");
        if (!string.IsNullOrWhiteSpace(show.Name))
        {
            b.AddContent(seq++, show.Name);
            b.AddContent(seq++, " · ");
        }
        b.AddContent(seq++, _loc.T("print.day", order.ShowDayNumber));
        if (show.DateOfOpening != default)
        {
            b.AddContent(seq++, " · ");
            b.AddContent(seq++, show.DateOfOpening.AddDays(order.ShowDayNumber - 1).ToString("yyyy-MM-dd"));
        }
        b.CloseElement();
        b.CloseElement();
    }

    private void BuildBandBlock(RenderTreeBuilder b, ref int seq, Band band, RunningOrderSlot slot, ContactRole role, Guid showId)
    {
        var stage = _bands.FindStage(showId, slot.StageId);
        var stageLabel = stage?.Name ?? _loc.T("page.runningOrder.unknownStage", slot.StageId);
        var contacts = band.Contacts.Where(c => c.Role == role).ToList();

        b.OpenElement(seq++, "section");
        b.AddAttribute(seq++, "class", "print-section mb-3");

        b.OpenElement(seq++, "h2");
        b.AddAttribute(seq++, "class", "h5");
        var title = $"{band.Name} — {(slot.OnStageTime?.ToString("HH:mm") ?? string.Empty)}";
        if (!string.IsNullOrEmpty(stageLabel))
            title += $" @ {stageLabel}";
        b.AddContent(seq++, title);
        b.CloseElement();

        // Contact(s) for the role
        if (contacts.Count == 0)
        {
            b.OpenElement(seq++, "div");
            b.AddAttribute(seq++, "class", "text-muted");
            b.AddContent(seq++, _loc.T("print.role.noContact"));
            b.CloseElement();
        }
        else
        {
            b.OpenElement(seq++, "table");
            b.AddAttribute(seq++, "class", "table table-sm");
            b.OpenElement(seq++, "thead");
            b.OpenElement(seq++, "tr");
            foreach (var h in new[] { _loc.T("field.name"), _loc.T("field.email"), _loc.T("field.phone") })
            {
                b.OpenElement(seq++, "th");
                b.AddContent(seq++, h);
                b.CloseElement();
            }
            b.CloseElement();
            b.CloseElement();
            b.OpenElement(seq++, "tbody");
            foreach (var c in contacts)
            {
                b.OpenElement(seq++, "tr");
                foreach (var v in new[] { c.Name, c.Email ?? string.Empty, c.Phone ?? string.Empty })
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

        BuildRoleSummary(b, ref seq, band, role);

        b.CloseElement();
    }

    private void BuildRoleSummary(RenderTreeBuilder b, ref int seq, Band band, ContactRole role)
    {
        var t = band.Rider.Tech;
        var fields = new List<(string Label, string Value)>();

        switch (role)
        {
            case ContactRole.FOHEngineer:
                AddIfSet(fields, _loc.T("print.band.field.fohConsole"), t.Foh.OwnConsoleModel);
                var protocolLabel = t.Foh.OutputProtocol == OutputProtocol.Other && !string.IsNullOrWhiteSpace(t.Foh.OutputProtocolOther) ? t.Foh.OutputProtocolOther! : _loc.T($"enum.OutputProtocol.{t.Foh.OutputProtocol}");
                var locationLabel = t.Foh.OutputLocation == OutputLocation.Other && !string.IsNullOrWhiteSpace(t.Foh.OutputLocationOther) ? t.Foh.OutputLocationOther! : _loc.T($"enum.OutputLocation.{t.Foh.OutputLocation}");
                fields.Add((_loc.T("print.band.field.fohOutput"), $"{protocolLabel} @ {locationLabel}"));
                AddIfSet(fields, _loc.T("field.foh.outputNotes"), t.Foh.OutputNotes);
                AddIfSet(fields, _loc.T("field.foh.additionalHardware"), t.Foh.AdditionalHardware);
                if (t.Foh.StageToFohSendCount > 0)
                {
                    var sends = $"{t.Foh.StageToFohSendCount}";
                    if (t.Foh.StageToFohRoundTripCount > 0)
                        sends += $" ({t.Foh.StageToFohRoundTripCount} {_loc.T("print.band.roundTrip")})";
                    fields.Add((_loc.T("field.foh.stageToFohSends"), sends));
                }
                if (t.Foh.FootprintWidthMeters is { } fw && t.Foh.FootprintLengthMeters is { } fl)
                    fields.Add((_loc.T("print.band.field.fohFootprint"), $"{fw}m × {fl}m"));
                AddIfSet(fields, _loc.T("print.band.field.fohNotes"), t.Foh.Notes);
                break;

            case ContactRole.MonitorEngineer:
                fields.Add((_loc.T("field.monitors.sourceMode"), _loc.T($"enum.MonitorSourceMode.{t.Monitors.SourceMode}")));
                if (t.Monitors.SourceMode == MonitorSourceMode.OwnConsole)
                {
                    AddIfSet(fields, _loc.T("print.band.field.monitorConsole"), t.Monitors.OwnConsoleModel);
                    fields.Add((_loc.T("field.monitors.consoleLocation"), _loc.T($"enum.MonitorTechLocation.{t.Monitors.OwnConsoleLocation}")));
                }
                fields.Add((_loc.T("field.monitors.wedges"), t.Monitors.Wedges.Count.ToString()));
                fields.Add((_loc.T("field.monitors.inEars"), t.Monitors.InEars.Count.ToString()));
                AddIfSet(fields, _loc.T("print.band.field.monitorNotes"), t.Monitors.Notes);
                break;

            case ContactRole.StageManager:
            case ContactRole.BackingTech:
                fields.Add((_loc.T("print.band.field.power"), $"{AmpLabel(t.Power.Amperage)} {PhaseLabel(t.Power.Phase)}"));
                AddIfSet(fields, _loc.T("field.power.adapterNotes"), t.Power.AdapterNotes);
                fields.Add((_loc.T("field.stage.risers"), t.Stage.Risers.Count.ToString()));
                fields.Add((_loc.T("field.stage.otherRisers"), t.Stage.OtherRisers.Count.ToString()));
                fields.Add((_loc.T("field.stage.wirelessMics"), t.Stage.WirelessMics.Sum(m => m.Count).ToString()));
                if (t.Stage.BringsOwnMics) fields.Add((_loc.T("field.stage.bringsOwnMics"), _loc.T("print.band.field.bringsOwnMics")));
                AddIfSet(fields, _loc.T("print.band.field.stageNotes"), t.Stage.Notes);
                break;

            case ContactRole.TourManager:
            case ContactRole.BandManager:
                var tp = band.TravelParty.Members;
                fields.Add((_loc.T("section.travelParty.title"), tp.Count.ToString()));
                AddIfSet(fields, _loc.T("field.notes"), band.Notes);
                break;
        }

        if (fields.Count == 0) return;

        b.OpenElement(seq++, "dl");
        b.AddAttribute(seq++, "class", "row mb-0");
        foreach (var (label, value) in fields)
        {
            b.OpenElement(seq++, "dt");
            b.AddAttribute(seq++, "class", "col-sm-4");
            b.AddContent(seq++, label);
            b.CloseElement();
            b.OpenElement(seq++, "dd");
            b.AddAttribute(seq++, "class", "col-sm-8");
            b.AddContent(seq++, value);
            b.CloseElement();
        }
        b.CloseElement();
    }

    private static void AddIfSet(List<(string, string)> fields, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) fields.Add((label, value!));
    }

    private string AmpLabel(PowerAmperage a) => _loc.T($"enum.PowerAmperage.{a}");

    private string PhaseLabel(PowerPhase p) => _loc.T($"enum.PowerPhase.{p}");
}
