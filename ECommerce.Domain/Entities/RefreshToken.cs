namespace ECommerce.Domain.Entities;

public class RefreshToken
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public string JwtId { get; set; } = string.Empty;
    public bool IsUsed { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime AddedDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    
    // Foreign key to ApplicationUser
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
}
