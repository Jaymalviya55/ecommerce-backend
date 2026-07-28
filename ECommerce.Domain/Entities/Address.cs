namespace ECommerce.Domain.Entities;

public class Address
{
    public int Id { get; set; }
    
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Pincode { get; set; } = string.Empty;
    public string Locality { get; set; } = string.Empty;
    public string StreetAddress { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Landmark { get; set; } = string.Empty;
    public string AlternatePhone { get; set; } = string.Empty;

    public string AddressType { get; set; } = "Home"; // Home or Work
    public bool IsDefault { get; set; } = false;
}
