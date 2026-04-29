using System.ComponentModel.DataAnnotations;

namespace FestivalRider.Models;

public class Band
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Notes { get; set; }
    public Rider Rider { get; set; } = new();
    public List<Contact> Contacts { get; set; } = new();
    public TravelParty TravelParty { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
