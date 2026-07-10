namespace ECommerce.Domain.Entities;

using System;
using System.ComponentModel.DataAnnotations;

public class Review
{
    public int Id { get; set; }
    
    [Required]
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    
    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    
    [Range(1, 5)]
    public int Rating { get; set; }
    
    [MaxLength(1000)]
    public string Comment { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
