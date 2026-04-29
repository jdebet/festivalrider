using System.ComponentModel.DataAnnotations;

namespace FestivalRider.Models;

public class RunningOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ShowId { get; set; }

    [Range(1, 31)]
    public int ShowDayNumber { get; set; } = 1;

    public List<RunningOrderSlot> Slots { get; set; } = new();
}
