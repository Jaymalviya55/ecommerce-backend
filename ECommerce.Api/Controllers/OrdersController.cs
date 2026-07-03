namespace ECommerce.Api.Controllers;

using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly ECommerceDbContext _context;

    public OrdersController(ECommerceDbContext context)
    {
        _context = context;
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
        var order = new Order
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

        _context.Orders.Add(order);

        // 4. Delete the Cart (it's been converted to an order)
        _context.Carts.Remove(cart);

        await _context.SaveChangesAsync();

        return Ok(new { OrderId = order.Id, TotalAmount = order.TotalAmount, Status = order.Status.ToString() });
    }
}

public class CheckoutRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}
