namespace ECommerce.Domain.Entities;

public class Cart
{
    public int Id { get; set; }
    
    // In a real app, this would be a UserId (Guid or string), but for now we'll use a session string
    public string SessionId { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}
