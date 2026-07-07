namespace ECommerce.Api.Controllers;

using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Razorpay.Api;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly ECommerceDbContext _context;
    private readonly IConfiguration _configuration;

    public OrdersController(ECommerceDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request)
    {
        // 1. Validate the cart exists and has items
        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.SessionId == request.SessionId);

        if (cart == null || !cart.Items.Any())
        {
            return BadRequest("Cart is empty or not found.");
        }

        // 2. Create the Order
        var order = new ECommerce.Domain.Entities.Order
        {
            CustomerEmail = request.Email,
            ShippingAddress = request.Address,
            Status = OrderStatus.Pending,
            TotalAmount = 0 // Will calculate safely below
        };

        // 3. Move CartItems to OrderItems and calculate total securely on backend
        foreach (var cartItem in cart.Items)
        {
            var product = await _context.Products.FindAsync(cartItem.ProductId);
            if (product == null) continue;

            var orderItem = new OrderItem
            {
                ProductId = cartItem.ProductId,
                Quantity = cartItem.Quantity,
                UnitPrice = product.Price // ALWAYS trust backend price, never frontend price!
            };

            order.TotalAmount += (orderItem.Quantity * orderItem.UnitPrice);
            order.Items.Add(orderItem);
            
            // Optionally, we could deduct StockQuantity here.
            // product.StockQuantity -= orderItem.Quantity;
        }

        // 4. Create Order on Razorpay Servers
        string keyId = _configuration["RazorpaySettings:KeyId"] ?? "";
        string keySecret = _configuration["RazorpaySettings:KeySecret"] ?? "";
        
        var client = new RazorpayClient(keyId, keySecret);
        
        // Razorpay expects the amount in the smallest currency sub-unit (Paise for INR, so * 100)
        long amountInPaise = (long)(order.TotalAmount * 100);

        var options = new Dictionary<string, object>
        {
            { "amount", amountInPaise },
            { "currency", "INR" },
            { "receipt", $"rcpt_{Guid.NewGuid().ToString().Substring(0, 8)}" }
        };

        Razorpay.Api.Order razorpayOrder = client.Order.Create(options);
        
        // Save the Razorpay Order ID securely in our database
        order.RazorpayOrderId = razorpayOrder["id"].ToString();

        _context.Orders.Add(order);

        // 5. Delete the Cart (it's been converted to an order)
        _context.Carts.Remove(cart);

        await _context.SaveChangesAsync();

        // Return the Razorpay Order ID and Public Key so the frontend can launch the modal
        return Ok(new { 
            OrderId = order.Id, 
            RazorpayOrderId = order.RazorpayOrderId,
            TotalAmount = order.TotalAmount,
            KeyId = keyId
        });
    }
}

public class CheckoutRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}
