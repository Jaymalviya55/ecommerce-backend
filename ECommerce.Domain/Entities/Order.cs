namespace ECommerce.Domain.Entities;

public enum OrderStatus
{
    Pending,
    Paid,
    Shipped,
    Delivered,
    Cancelled
}

public class Order
{
    public int Id { get; set; }
    
    // Guest Checkout Details
    public string CustomerEmail { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    // Razorpay Integration Fields (Explained below)
    // When a user clicks checkout, we create an order on Razorpay's servers. They give us a unique Order ID.
    // We store it here so we know this database record is tied to that specific payment attempt.
    public string RazorpayOrderId { get; set; } = string.Empty;
    
    // When the user actually types in their card and pays successfully, Razorpay gives us a Payment ID.
    // We store it here for accounting and refund purposes.
    public string RazorpayPaymentId { get; set; } = string.Empty;

    public string? TrackingNumber { get; set; }
    public string? CarrierName { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
