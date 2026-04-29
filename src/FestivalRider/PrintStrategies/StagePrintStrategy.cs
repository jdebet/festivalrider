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

    public StagePrintStrategy(IBandService bands)
    {
        _bands = bands;
    }

    public string Key => "stage";

    public string GetTitle(object context)
    {
        var (order, stage, _) = Resolve(context);
        var show = _bands.Snapshot().ShowData;
        var date = show.DateOfOpening == default
            ? $"Day {order.ShowDayNumber}"
            : show.DateOfOpening.AddDays(order.ShowDayNumber - 1).ToString("ddd dd MMM yyyy");
        return string.IsNullOrWhiteSpace(show.Name)
            ? $"{stage.Name} — {date}"
            : $"{show.Name} — {stage.Name} — {date}";
    }

    public RenderFragment Render(object context)
    {
        var (order, stage, slots) = Resolve(context);
        var show = _bands.Snapshot().ShowData;
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
        var stage = _bands.FindStage(ctx.StageId)
            ?? throw new InvalidOperationException($"Stage {ctx.StageId} not found.");
        var slots = order.Slots
            .Where(s => s.StageId == ctx.StageId)
            .OrderBy(s => s.StartTime)
            .ToList();
        return (order, stage, slots);
    }

    private static void BuildHeader(RenderTreeBuilder b, ref int seq, Stage stage, RunningOrder order, ShowData show)
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
        b.AddContent(seq++, $"Day {order.ShowDayNumber}");
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
        b.AddContent(seq++, "Schedule");
        b.CloseElement();

        if (slots.Count == 0)
        {
            b.OpenElement(seq++, "p");
            b.AddContent(seq++, "No slots on this stage.");
            b.CloseElement();
            b.CloseElement();
            return;
        }

        b.OpenElement(seq++, "table");
        b.AddAttribute(seq++, "class", "table table-sm");
        b.OpenElement(seq++, "thead");
        b.OpenElement(seq++, "tr");
        foreach (var h in new[] { "Start", "Set", "Changeover", "Band", "Notes" })
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
                $"{s.SetLengthMinutes} min",
                $"{s.ChangeoverMinutes} min",
                band?.Name ?? "Unknown band",
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
        b.AddContent(seq++, "Tech summary");
        b.CloseElement();

        b.OpenElement(seq++, "table");
        b.AddAttribute(seq++, "class", "table table-sm");
        b.OpenElement(seq++, "thead");
        b.OpenElement(seq++, "tr");
        foreach (var h in new[] { "Start", "Band", "Power", "FOH console", "Monitors", "Wedges", "IEMs", "Risers" })
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
                band?.Name ?? "Unknown band",
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

    private static string AmpLabel(PowerAmperage a) => a switch
    {
        PowerAmperage._16_A => "16A",
        PowerAmperage._32_A => "32A",
        PowerAmperage._63_A => "63A",
        _ => a.ToString(),
    };

    private static string PhaseLabel(PowerPhase p) => p == PowerPhase.ThreePhase ? "3φ" : "1φ";

    private static string MonitorLabel(MonitorSetup m) => m.SourceMode switch
    {
        MonitorSourceMode.OwnConsole => string.IsNullOrWhiteSpace(m.OwnConsoleModel) ? "Own" : $"Own ({m.OwnConsoleModel})",
        MonitorSourceMode.FromFoh => "From FOH",
        _ => "—",
    };
}
