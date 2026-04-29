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

    public RolePrintStrategy(IBandService bands)
    {
        _bands = bands;
    }

    public string Key => "role";

    public string GetTitle(object context)
    {
        var (order, role, _) = Resolve(context);
        var show = _bands.FindShow(order.ShowId) ?? new ShowData();
        var date = show.DateOfOpening == default
            ? $"Day {order.ShowDayNumber}"
            : show.DateOfOpening.AddDays(order.ShowDayNumber - 1).ToString("ddd dd MMM yyyy");
        return string.IsNullOrWhiteSpace(show.Name)
            ? $"{RoleLabel(role)} — {date}"
            : $"{show.Name} — {RoleLabel(role)} — {date}";
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
                builder.AddContent(seq++, "No slots scheduled.");
                builder.CloseElement();
                return;
            }
            foreach (var s in slots)
            {
                var band = _bands.FindBand(s.BandId);
                if (band is null) continue;
                BuildBandBlock(builder, ref seq, band, s, role);
            }
        };
    }

    private (RunningOrder order, ContactRole role, IReadOnlyList<RunningOrderSlot> slots) Resolve(object context)
    {
        if (context is not RoleContext ctx)
            throw new ArgumentException($"RolePrintStrategy expects RoleContext, got {context?.GetType().Name ?? "null"}.", nameof(context));
        var order = _bands.FindRunningOrder(ctx.RunningOrderId)
            ?? throw new InvalidOperationException($"Running order {ctx.RunningOrderId} not found.");
        var slots = order.Slots.OrderBy(s => s.StartTime).ToList();
        return (order, ctx.Role, slots);
    }

    private static void BuildHeader(RenderTreeBuilder b, ref int seq, ContactRole role, RunningOrder order, ShowData show)
    {
        b.OpenElement(seq++, "header");
        b.AddAttribute(seq++, "class", "print-section mb-3");
        b.OpenElement(seq++, "h1");
        b.AddAttribute(seq++, "class", "h3");
        b.AddContent(seq++, RoleLabel(role));
        b.CloseElement();
        b.OpenElement(seq++, "div");
        b.AddAttribute(seq++, "class", "text-muted");
        if (!string.IsNullOrWhiteSpace(show.Name))
        {
            b.AddContent(seq++, show.Name);
            b.AddContent(seq++, " · ");
        }
        b.AddContent(seq++, $"Day {order.ShowDayNumber}");
        if (show.DateOfOpening != default)
        {
            b.AddContent(seq++, " · ");
            b.AddContent(seq++, show.DateOfOpening.AddDays(order.ShowDayNumber - 1).ToString("yyyy-MM-dd"));
        }
        b.CloseElement();
        b.CloseElement();
    }

    private void BuildBandBlock(RenderTreeBuilder b, ref int seq, Band band, RunningOrderSlot slot, ContactRole role)
    {
        var stage = _bands.FindStage(slot.StageId);
        var stageLabel = stage?.Name ?? $"Unknown stage (#{slot.StageId})";
        var contacts = band.Contacts.Where(c => c.Role == role).ToList();

        b.OpenElement(seq++, "section");
        b.AddAttribute(seq++, "class", "print-section mb-3");

        b.OpenElement(seq++, "h2");
        b.AddAttribute(seq++, "class", "h5");
        b.AddContent(seq++, $"{band.Name} — {slot.StartTime:HH\\:mm} @ {stageLabel}");
        b.CloseElement();

        // Contact(s) for the role
        if (contacts.Count == 0)
        {
            b.OpenElement(seq++, "div");
            b.AddAttribute(seq++, "class", "text-muted");
            b.AddContent(seq++, "No contact for this role.");
            b.CloseElement();
        }
        else
        {
            b.OpenElement(seq++, "table");
            b.AddAttribute(seq++, "class", "table table-sm");
            b.OpenElement(seq++, "thead");
            b.OpenElement(seq++, "tr");
            foreach (var h in new[] { "Name", "Email", "Phone" })
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

    private static void BuildRoleSummary(RenderTreeBuilder b, ref int seq, Band band, ContactRole role)
    {
        var t = band.Rider.Tech;
        var fields = new List<(string Label, string Value)>();

        switch (role)
        {
            case ContactRole.FOHEngineer:
                AddIfSet(fields, "FOH console", t.Foh.OwnConsoleModel);
                fields.Add(("FOH output", $"{t.Foh.OutputProtocol} @ {t.Foh.OutputLocation}"));
                AddIfSet(fields, "Output notes", t.Foh.OutputNotes);
                AddIfSet(fields, "Additional hardware", t.Foh.AdditionalHardware);
                if (t.Foh.StageToFohSendCount > 0)
                    fields.Add(("Stage→FOH sends", $"{t.Foh.StageToFohSendCount}{(t.Foh.StageToFohRoundTrip ? " (round trip)" : "")}"));
                if (t.Foh.FootprintWidthMeters is { } fw && t.Foh.FootprintLengthMeters is { } fl)
                    fields.Add(("FOH footprint", $"{fw}m × {fl}m"));
                AddIfSet(fields, "FOH notes", t.Foh.Notes);
                break;

            case ContactRole.MonitorEngineer:
                fields.Add(("Source", t.Monitors.SourceMode.ToString()));
                if (t.Monitors.SourceMode == MonitorSourceMode.OwnConsole)
                {
                    AddIfSet(fields, "Monitor console", t.Monitors.OwnConsoleModel);
                    fields.Add(("Console location", t.Monitors.OwnConsoleLocation.ToString()));
                }
                fields.Add(("Wedges", t.Monitors.Wedges.Count.ToString()));
                fields.Add(("In-ears", t.Monitors.InEars.Count.ToString()));
                AddIfSet(fields, "Monitor notes", t.Monitors.Notes);
                break;

            case ContactRole.StageManager:
            case ContactRole.BackingTech:
                fields.Add(("Power", $"{AmpLabel(t.Power.Amperage)} {PhaseLabel(t.Power.Phase)}"));
                AddIfSet(fields, "Adapter notes", t.Power.AdapterNotes);
                fields.Add(("Risers", t.Stage.Risers.Count.ToString()));
                fields.Add(("Other risers", t.Stage.OtherRisers.Count.ToString()));
                fields.Add(("Wireless mics", t.Stage.WirelessMics.Sum(m => m.Count).ToString()));
                if (t.Stage.BringsOwnMics) fields.Add(("Mics", "Brings own"));
                AddIfSet(fields, "Stage notes", t.Stage.Notes);
                break;

            case ContactRole.TourManager:
            case ContactRole.BandManager:
                var tp = band.TravelParty.Members;
                fields.Add(("Travel party", tp.Count.ToString()));
                AddIfSet(fields, "Band notes", band.Notes);
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

    private static string AmpLabel(PowerAmperage a) => a switch
    {
        PowerAmperage._16_A => "16A",
        PowerAmperage._32_A => "32A",
        PowerAmperage._63_A => "63A",
        _ => a.ToString(),
    };

    private static string PhaseLabel(PowerPhase p) => p == PowerPhase.ThreePhase ? "3-phase" : "single-phase";

    private static string RoleLabel(ContactRole r) => r switch
    {
        ContactRole.TourManager => "Tour manager",
        ContactRole.BandManager => "Band manager",
        ContactRole.FOHEngineer => "FOH engineer",
        ContactRole.MonitorEngineer => "Monitor engineer",
        ContactRole.StageManager => "Stage manager",
        ContactRole.BackingTech => "Backing tech",
        _ => r.ToString(),
    };
}
