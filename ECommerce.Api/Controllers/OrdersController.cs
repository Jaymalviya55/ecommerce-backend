namespace ECommerce.Api.Controllers;

using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
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
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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
            UserId = userId,
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

    [Authorize(Roles = "Admin,SupportAgent")]
    [HttpGet("all")]
    public async Task<IActionResult> GetAllOrders()
    {
        var orders = await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new {
                o.Id,
                o.OrderDate,
                o.CustomerEmail,
                o.TotalAmount,
                Status = o.Status.ToString(),
                o.TrackingNumber,
                o.CarrierName,
                Items = o.Items.Select(i => new {
                    i.ProductId,
                    ProductName = i.Product != null ? i.Product.Name : "Unknown Product",
                    i.Quantity,
                    i.UnitPrice
                })
            })
            .ToListAsync();

        return Ok(orders);
    }

    [Authorize(Roles = "Admin,FulfillmentStaff")]
    [HttpGet("fulfillment")]
    public async Task<IActionResult> GetFulfillmentOrders()
    {
        var orders = await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .Where(o => o.Status == OrderStatus.Paid || o.Status == OrderStatus.Pending)
            .OrderBy(o => o.OrderDate)
            .Select(o => new {
                o.Id,
                o.OrderDate,
                o.CustomerEmail,
                o.ShippingAddress,
                o.TotalAmount,
                Status = o.Status.ToString(),
                Items = o.Items.Select(i => new {
                    i.ProductId,
                    ProductName = i.Product != null ? i.Product.Name : "Unknown Product",
                    i.Quantity
                })
            })
            .ToListAsync();

        return Ok(orders);
    }

    [Authorize(Roles = "Admin,FulfillmentStaff")]
    [HttpPut("{id}/ship")]
    public async Task<IActionResult> ShipOrder(int id, [FromBody] ShipOrderRequest request)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null) return NotFound("Order not found.");

        if (order.Status != OrderStatus.Pending && order.Status != OrderStatus.Paid)
        {
            return BadRequest("Only Pending or Paid orders can be shipped.");
        }

        order.Status = OrderStatus.Shipped;
        order.TrackingNumber = request.TrackingNumber;
        order.CarrierName = request.CarrierName;

        await _context.SaveChangesAsync();
        return Ok(new { Message = "Order marked as shipped." });
    }

    [Authorize(Roles = "Admin,FulfillmentStaff")]
    [HttpPut("{id}/deliver")]
    public async Task<IActionResult> DeliverOrder(int id)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null) return NotFound("Order not found.");

        if (order.Status != OrderStatus.Shipped)
        {
            return BadRequest("Only Shipped orders can be marked as delivered.");
        }

        order.Status = OrderStatus.Delivered;
        await _context.SaveChangesAsync();
        return Ok(new { Message = "Order marked as delivered." });
    }

    [Authorize(Roles = "Admin,SupportAgent,Customer")]
    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> CancelOrder(int id)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);
            
        if (order == null) return NotFound("Order not found.");

        if (order.Status == OrderStatus.Shipped || order.Status == OrderStatus.Delivered)
        {
            return BadRequest("Cannot cancel an order that has already been shipped or delivered.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return BadRequest("Order is already cancelled.");
        }

        // Restock inventory
        foreach (var item in order.Items)
        {
            var product = await _context.Products.FindAsync(item.ProductId);
            if (product != null)
            {
                product.StockQuantity += item.Quantity;
            }
        }

        order.Status = OrderStatus.Cancelled;
        await _context.SaveChangesAsync();
        
        return Ok(new { Message = "Order cancelled and inventory restocked." });
    }

    [Authorize]
    [HttpGet("my-orders")]
    public async Task<IActionResult> GetMyOrders()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        
        if (string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(email))
        {
            return Unauthorized("User identity not found in token.");
        }

        var query = _context.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .AsQueryable();
            
        if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(email))
        {
            query = query.Where(o => o.UserId == userId || o.CustomerEmail == email);
        }
        else if (!string.IsNullOrEmpty(userId))
        {
            query = query.Where(o => o.UserId == userId);
        }
        else
        {
            query = query.Where(o => o.CustomerEmail == email);
        }

        var orders = await query
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new {
                o.Id,
                o.OrderDate,
                o.TotalAmount,
                Status = o.Status.ToString(),
                o.TrackingNumber,
                o.CarrierName,
                Items = o.Items.Select(i => new {
                    i.ProductId,
                    ProductName = i.Product != null ? i.Product.Name : "Unknown Product",
                    i.Quantity,
                    i.UnitPrice
                })
            })
            .ToListAsync();

        return Ok(orders);
    }
}

public class CheckoutRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

public class ShipOrderRequest
{
    public string? TrackingNumber { get; set; }
    public string? CarrierName { get; set; }
}
