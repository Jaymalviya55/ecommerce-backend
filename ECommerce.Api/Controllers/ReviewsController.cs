namespace ECommerce.Api.Controllers;

using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Linq;
using System.ComponentModel.DataAnnotations;

[ApiController]
[Route("api/products/{productId}/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly ECommerceDbContext _context;

    public ReviewsController(ECommerceDbContext context)
    {
        _context = context;
    }

    private string? CurrentUserId => User.FindFirst("uid")?.Value;

    [HttpGet]
    public async Task<IActionResult> GetReviews(int productId)
    {
        var reviews = await _context.Reviews
            .Include(r => r.User)
            .Where(r => r.ProductId == productId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new {
                r.Id,
                r.Rating,
                r.Comment,
                r.CreatedAt,
                r.UpdatedAt,
                r.UserId,
                // Safely extract name from email for privacy
                UserName = r.User != null && r.User.UserName != null 
                    ? r.User.UserName.Split('@', StringSplitOptions.None)[0] 
                    : "Anonymous"
            })
            .ToListAsync();

        var totalReviews = reviews.Count;
        var averageRating = totalReviews > 0 ? Math.Round(reviews.Average(r => r.Rating), 1) : 0;

        return Ok(new {
            AverageRating = averageRating,
            TotalReviews = totalReviews,
            Reviews = reviews
        });
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> AddReview(int productId, [FromBody] ReviewDto request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var existingReview = await _context.Reviews
            .FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == userId);

        if (existingReview != null)
        {
            return BadRequest("You have already reviewed this product.");
        }

        // Validate product exists
        if (!await _context.Products.AnyAsync(p => p.Id == productId))
        {
            return NotFound("Product not found.");
        }

        var review = new Review
        {
            ProductId = productId,
            UserId = userId,
            Rating = request.Rating,
            Comment = request.Comment,
            CreatedAt = DateTime.UtcNow
        };

        _context.Reviews.Add(review);
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Review added successfully." });
    }

    [Authorize]
    [HttpPut("{reviewId}")]
    public async Task<IActionResult> UpdateReview(int productId, int reviewId, [FromBody] ReviewDto request)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var review = await _context.Reviews
            .FirstOrDefaultAsync(r => r.Id == reviewId && r.ProductId == productId);

        if (review == null) return NotFound("Review not found.");

        if (review.UserId != userId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        review.Rating = request.Rating;
        review.Comment = request.Comment;
        review.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { Message = "Review updated successfully." });
    }
}

public class ReviewDto
{
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
    public int Rating { get; set; }
    
    [Required]
    [MaxLength(1000)]
    public string Comment { get; set; } = string.Empty;
}
