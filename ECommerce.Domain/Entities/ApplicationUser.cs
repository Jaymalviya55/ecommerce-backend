using Microsoft.AspNetCore.Identity;

namespace ECommerce.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    // We can add custom properties here later (e.g., FullName, Address)
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
