using System.ComponentModel.DataAnnotations;

namespace FestivalRider.Models;

public class Contact
{
    public ContactRole Role { get; set; }
    public string Name { get; set; } = string.Empty;

    [EmailAddress]
    public string? Email { get; set; }

    public string? Phone { get; set; }
}
