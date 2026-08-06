namespace ECommerce.Api.Controllers;

using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using ECommerce.Api.MetadataHolder;

[ApiController]
[Route("api/[controller]")]
public class CouponsController : ControllerBase
{
    private readonly ECommerceDbContext _context;

    public CouponsController(ECommerceDbContext context)
    {
        _context = context;
    }

    [Authorize]
    [FeatureAuthorization("@admin/coupons", "read")]
    [HttpGet]
    public async Task<IActionResult> GetCoupons()
    {
        var coupons = await _context.Coupons.OrderByDescending(c => c.CreatedAt).ToListAsync();
        return Ok(coupons);
    }

    [Authorize]
    [HttpGet("active")]
    public async Task<IActionResult> GetActiveCoupons()
    {
        var userId = User.FindFirst("uid")?.Value;
        var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        // Fetch coupons assigned to this user, or global active coupons
        var couponsQuery = await _context.Coupons
            .Where(c => (c.IsActive && c.ExpirationDate > DateTime.UtcNow && c.AssignedToUserId == null && c.AssignedToEmail == null) ||
                        (c.AssignedToUserId == userId || c.AssignedToEmail == userEmail))
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        // Check which coupons the user has already used
        var usedCouponCodes = await _context.Orders
            .Where(o => (o.UserId == userId || o.CustomerEmail == userEmail) && o.AppliedCouponCode != null && o.Status != OrderStatus.Cancelled)
            .Select(o => o.AppliedCouponCode)
            .Distinct()
            .ToListAsync();

        var result = couponsQuery.Select(c => new
        {
            c.Id,
            c.Code,
            c.DiscountPercentage,
            c.IsActive,
            c.ExpirationDate,
            c.MinimumSpend,
            c.CreatedAt,
            c.AssignedToUserId,
            c.AssignedToEmail,
            IsUsed = !c.IsActive || usedCouponCodes.Contains(c.Code)
        });

        return Ok(result);
    }

    [Authorize]
    [FeatureAuthorization("@admin/coupons", "write")]
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
            AssignedToEmail = string.IsNullOrWhiteSpace(request.AssignedToEmail) ? null : request.AssignedToEmail.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _context.Coupons.Add(coupon);
        await _context.SaveChangesAsync();

        return Ok(coupon);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateCouponStatus(int id, [FromBody] bool isActive)
    {
        var coupon = await _context.Coupons.FindAsync(id);
        if (coupon == null)
            return NotFound("Coupon not found.");

        coupon.IsActive = isActive;
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
    public string? AssignedToEmail { get; set; }
}
