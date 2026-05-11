using FestivalRider.Models;
using FestivalRider.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace FestivalRider.PrintStrategies;

// Key "stage". Context: StageContext(runningOrderId, stageId). Renders one stage's schedule for a show day
// with compact per-slot tech summary (power, FOH console, monitor source, stage footprint).
public class StagePrintStrategy : IPrintStrategy
{
    private readonly IBandService _bands;
    private readonly ILocalizationService _loc;

    public StagePrintStrategy(IBandService bands, ILocalizationService loc)
    {
        _bands = bands;
        _loc = loc;
    }

    public string Key => "stage";

    public string GetTitle(object context)
    {
        var (order, stage, _) = Resolve(context);
        var show = _bands.FindShow(order.ShowId) ?? new ShowData();
        var date = show.DateOfOpening == default
            ? _loc.T("print.day", order.ShowDayNumber)
            : show.DateOfOpening.AddDays(order.ShowDayNumber - 1).ToString("ddd dd MMM yyyy", _loc.Culture);
        return string.IsNullOrWhiteSpace(show.Name)
            ? $"{stage.Name} — {date}"
            : $"{show.Name} — {stage.Name} — {date}";
    }

    public RenderFragment Render(object context)
    {
        var (order, stage, slots) = Resolve(context);
        var show = _bands.FindShow(order.ShowId) ?? new ShowData();
        return builder =>
        {
            var seq = 0;
            BuildHeader(builder, ref seq, stage, order, show);
            BuildScheduleTable(builder, ref seq, slots);
            BuildTechSummary(builder, ref seq, slots);
        };
    }

    private (RunningOrder order, Stage stage, IReadOnlyList<RunningOrderSlot> slots) Resolve(object context)
    {
        if (context is not StageContext ctx)
            throw new ArgumentException($"StagePrintStrategy expects StageContext, got {context?.GetType().Name ?? "null"}.", nameof(context));
        var order = _bands.FindRunningOrder(ctx.RunningOrderId)
            ?? throw new InvalidOperationException($"Running order {ctx.RunningOrderId} not found.");
        var stage = _bands.FindStage(order.ShowId, ctx.StageId)
            ?? throw new InvalidOperationException($"Stage {ctx.StageId} not found in show {order.ShowId}.");
        var slots = order.Slots
            .Where(s => s.StageId == ctx.StageId)
            .OrderBy(s => s.StartTime)
            .ToList();
        return (order, stage, slots);
    }

    private void BuildHeader(RenderTreeBuilder b, ref int seq, Stage stage, RunningOrder order, ShowData show)
    {
        b.OpenElement(seq++, "header");
        b.AddAttribute(seq++, "class", "print-section mb-3");
        b.OpenElement(seq++, "h1");
        b.AddAttribute(seq++, "class", "h3");
        b.AddContent(seq++, stage.Name);
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

    private void BuildScheduleTable(RenderTreeBuilder b, ref int seq, IReadOnlyList<RunningOrderSlot> slots)
    {
        b.OpenElement(seq++, "section");
        b.AddAttribute(seq++, "class", "print-section mb-3");
        b.OpenElement(seq++, "h2");
        b.AddAttribute(seq++, "class", "h5");
        b.AddContent(seq++, _loc.T("print.stage.scheduleHeading"));
        b.CloseElement();

        if (slots.Count == 0)
        {
            b.OpenElement(seq++, "p");
            b.AddContent(seq++, _loc.T("print.stage.noSlots"));
            b.CloseElement();
            b.CloseElement();
            return;
        }

        b.OpenElement(seq++, "table");
        b.AddAttribute(seq++, "class", "table table-sm");
        b.OpenElement(seq++, "thead");
        b.OpenElement(seq++, "tr");
        foreach (var h in new[] { _loc.T("print.stage.col.start"), _loc.T("print.stage.col.set"), _loc.T("print.stage.col.changeover"), _loc.T("print.stage.col.band"), _loc.T("print.stage.col.notes") })
        {
            b.OpenElement(seq++, "th");
            b.AddContent(seq++, h);
            b.CloseElement();
        }
        b.CloseElement();
        b.CloseElement();
        b.OpenElement(seq++, "tbody");
        foreach (var s in slots)
        {
            var band = _bands.FindBand(s.BandId);
            b.OpenElement(seq++, "tr");
            foreach (var v in new[]
            {
                s.StartTime.ToString("HH:mm"),
                _loc.T("print.stage.min", s.SetLengthMinutes),
                _loc.T("print.stage.min", s.ChangeoverMinutes),
                band?.Name ?? _loc.T("print.stage.unknownBand"),
                s.Notes ?? string.Empty,
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
        b.CloseElement();
    }

    private void BuildTechSummary(RenderTreeBuilder b, ref int seq, IReadOnlyList<RunningOrderSlot> slots)
    {
        if (slots.Count == 0) return;
        b.OpenElement(seq++, "section");
        b.AddAttribute(seq++, "class", "print-section mb-3");
        b.OpenElement(seq++, "h2");
        b.AddAttribute(seq++, "class", "h5");
        b.AddContent(seq++, _loc.T("print.stage.techSummaryHeading"));
        b.CloseElement();

        b.OpenElement(seq++, "table");
        b.AddAttribute(seq++, "class", "table table-sm");
        b.OpenElement(seq++, "thead");
        b.OpenElement(seq++, "tr");
        foreach (var h in new[] { _loc.T("print.stage.col.start"), _loc.T("print.stage.col.band"), _loc.T("print.stage.col.power"), _loc.T("print.stage.col.fohConsole"), _loc.T("print.stage.col.monitors"), _loc.T("print.stage.col.wedges"), _loc.T("print.stage.col.iems"), _loc.T("print.stage.col.risers") })
        {
            b.OpenElement(seq++, "th");
            b.AddContent(seq++, h);
            b.CloseElement();
        }
        b.CloseElement();
        b.CloseElement();
        b.OpenElement(seq++, "tbody");
        foreach (var s in slots)
        {
            var band = _bands.FindBand(s.BandId);
            var t = band?.Rider.Tech;
            b.OpenElement(seq++, "tr");
            foreach (var v in new[]
            {
                s.StartTime.ToString("HH:mm"),
                band?.Name ?? _loc.T("print.stage.unknownBand"),
                t is null ? string.Empty : $"{AmpLabel(t.Power.Amperage)} {PhaseLabel(t.Power.Phase)}",
                t?.Foh.OwnConsoleModel ?? string.Empty,
                t is null ? string.Empty : MonitorLabel(t.Monitors),
                t is null ? string.Empty : t.Monitors.Wedges.Count.ToString(),
                t is null ? string.Empty : t.Monitors.InEars.Count.ToString(),
                t is null ? string.Empty : t.Stage.Risers.Count.ToString(),
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
        b.CloseElement();
    }

    private string AmpLabel(PowerAmperage a) => _loc.T($"enum.PowerAmperage.{a}");

    private string PhaseLabel(PowerPhase p) => _loc.T($"enum.PowerPhase.{p}");

    private string MonitorLabel(MonitorSetup m)
    {
        var label = _loc.T($"enum.MonitorSourceMode.{m.SourceMode}");
        if (m.SourceMode == MonitorSourceMode.OwnConsole && !string.IsNullOrWhiteSpace(m.OwnConsoleModel))
            return $"{label} ({m.OwnConsoleModel})";
        return label;
    }
}
