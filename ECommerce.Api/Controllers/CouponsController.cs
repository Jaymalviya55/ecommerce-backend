namespace ECommerce.Api.Controllers;

using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

[ApiController]
[Route("api/[controller]")]
public class CouponsController : ControllerBase
{
    private readonly ECommerceDbContext _context;

    public CouponsController(ECommerceDbContext context)
    {
        _context = context;
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> GetCoupons()
    {
        var coupons = await _context.Coupons.OrderByDescending(c => c.CreatedAt).ToListAsync();
        return Ok(coupons);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> CreateCoupon([FromBody] CreateCouponDto request)
    {
        var existing = await _context.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == request.Code.ToUpper());
        if (existing != null)
        {
            return BadRequest("A coupon with this code already exists.");
        }

        var coupon = new Coupon
        {
            Code = request.Code.ToUpper(),
            DiscountPercentage = request.DiscountPercentage,
            IsActive = request.IsActive,
            ExpirationDate = request.ExpirationDate,
            MinimumSpend = request.MinimumSpend,
            CreatedAt = DateTime.UtcNow
        };

        _context.Coupons.Add(coupon);
        await _context.SaveChangesAsync();

        return Ok(coupon);
    }
}

public class CreateCouponDto
{
    public string Code { get; set; } = string.Empty;
    public decimal DiscountPercentage { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime ExpirationDate { get; set; }
    public decimal MinimumSpend { get; set; }
}
