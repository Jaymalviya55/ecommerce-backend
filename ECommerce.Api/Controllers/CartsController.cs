namespace ECommerce.Api.Controllers;

using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class CartsController : ControllerBase
{
    private readonly ECommerceDbContext _context;

    public CartsController(ECommerceDbContext context)
    {
        _context = context;
    }

    [HttpGet("{sessionId}")]
    public async Task<IActionResult> GetCart(string sessionId)
    {
        var cart = await _context.Carts
            .Include(c => c.Items)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(c => c.SessionId == sessionId);

        if (cart == null)
        {
            // If the cart doesn't exist yet, we just return an empty object to the frontend
            return Ok(new { SessionId = sessionId, Items = new List<object>() });
        }

        return Ok(cart);
    }

    [HttpPost("{sessionId}/items")]
    public async Task<IActionResult> AddItemToCart(string sessionId, [FromBody] AddToCartRequest request)
    {
        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.SessionId == sessionId);

        if (cart == null)
        {
            cart = new Cart { SessionId = sessionId };
            _context.Carts.Add(cart);
        }

        var product = await _context.Products.FindAsync(request.ProductId);
        if (product == null) return NotFound("Product not found");

        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId);
        if (existingItem != null)
        {
            existingItem.Quantity += request.Quantity;
        }
        else
        {
            cart.Items.Add(new CartItem
            {
                ProductId = request.ProductId,
                Quantity = request.Quantity,
                UnitPrice = product.Price
            });
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Refetch to include product details for the response
        var updatedCart = await _context.Carts
            .Include(c => c.Items)
            .ThenInclude(i => i.Product)
            .FirstAsync(c => c.Id == cart.Id);

        return Ok(updatedCart);
    }

    [HttpPut("{sessionId}/items/{productId}")]
    public async Task<IActionResult> UpdateItemQuantity(string sessionId, int productId, [FromBody] UpdateQuantityRequest request)
    {
        var cart = await _context.Carts
            .Include(c => c.Items)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(c => c.SessionId == sessionId);

        if (cart == null) return NotFound();

        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            if (request.Quantity <= 0)
            {
                _context.CartItems.Remove(item);
            }
            else
            {
                item.Quantity = request.Quantity;
            }
            cart.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return Ok(cart);
    }

    [HttpDelete("{sessionId}/items/{productId}")]
    public async Task<IActionResult> RemoveItemFromCart(string sessionId, int productId)
    {
        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.SessionId == sessionId);

        if (cart == null) return NotFound();

        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            _context.CartItems.Remove(item);
            cart.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return NoContent();
    }

    [HttpPost("{sessionId}/coupon")]
    public async Task<IActionResult> ApplyCoupon(string sessionId, [FromBody] ApplyCouponRequest request)
    {
        var cart = await _context.Carts
            .Include(c => c.Items)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(c => c.SessionId == sessionId);

        if (cart == null || !cart.Items.Any())
        {
            return BadRequest("Cart is empty or not found.");
        }

        // Validate Coupon
        var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == request.Code.ToUpper());
        if (coupon == null || !coupon.IsActive || coupon.ExpirationDate < DateTime.UtcNow)
        {
            return BadRequest("Invalid or expired coupon code.");
        }

        var subtotal = cart.Items.Sum(i => i.Quantity * i.UnitPrice);
        if (subtotal < coupon.MinimumSpend)
        {
            return BadRequest($"This coupon requires a minimum spend of ₹{coupon.MinimumSpend}.");
        }
        
        // Ensure user hasn't used this coupon already (One-time use per user rule)
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

        if (!string.IsNullOrEmpty(userId) || !string.IsNullOrEmpty(email))
        {
            var alreadyUsed = await _context.Orders
                .AnyAsync(o => (o.UserId == userId || o.CustomerEmail == email) 
                               && o.AppliedCouponCode == coupon.Code 
                               && o.Status != OrderStatus.Cancelled);
                               
            if (alreadyUsed)
            {
                return BadRequest("You have already used this coupon code.");
            }
        }

        cart.AppliedCouponCode = coupon.Code;
        cart.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Coupon applied successfully.", CouponCode = coupon.Code, DiscountPercentage = coupon.DiscountPercentage });
    }

    [HttpDelete("{sessionId}/coupon")]
    public async Task<IActionResult> RemoveCoupon(string sessionId)
    {
        var cart = await _context.Carts.FirstOrDefaultAsync(c => c.SessionId == sessionId);
        if (cart == null) return NotFound();

        cart.AppliedCouponCode = null;
        cart.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Coupon removed." });
    }
}

public class ApplyCouponRequest
{
    public string Code { get; set; } = string.Empty;
}

public class AddToCartRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public class UpdateQuantityRequest
{
    public int Quantity { get; set; }
}
