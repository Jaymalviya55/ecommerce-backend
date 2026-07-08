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
