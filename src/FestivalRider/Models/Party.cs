using System.ComponentModel.DataAnnotations;

namespace FestivalRider.Models;

public class Party
{
    public PartyType Type { get; set; }
    public string Role { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;
}
