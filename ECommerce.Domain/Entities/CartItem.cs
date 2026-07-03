namespace ECommerce.Domain.Entities;

public class CartItem
{
    public int Id { get; set; }
    
    public int CartId { get; set; }
    public Cart? Cart { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int Quantity { get; set; }
    
    // We snapshot the price when it's added to the cart
    public decimal UnitPrice { get; set; }
}
