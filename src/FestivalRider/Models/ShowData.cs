using System.ComponentModel.DataAnnotations;

namespace FestivalRider.Models;

public class ShowData
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }
    public DateOnly DateOfOpening { get; set; }

    [Range(1, 31)]
    public int ShowDayCount { get; set; } = 1;

    public List<Stage> Stages { get; set; } = new();
    public List<Band> Bands { get; set; } = new();
    public List<RunningOrder> RunningOrders { get; set; } = new();
}
