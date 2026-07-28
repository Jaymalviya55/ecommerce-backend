namespace ECommerce.Domain.Entities;

public class Coupon
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public decimal DiscountPercentage { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime ExpirationDate { get; set; }
    public decimal MinimumSpend { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
