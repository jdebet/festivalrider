using System.ComponentModel.DataAnnotations;

namespace FestivalRider.Models;

public class Stage
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;
}
