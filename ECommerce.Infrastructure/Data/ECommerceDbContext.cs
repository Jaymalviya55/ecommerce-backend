namespace ECommerce.Infrastructure.Data;

using ECommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

public class ECommerceDbContext : IdentityDbContext<ApplicationUser>
{
    public ECommerceDbContext(DbContextOptions<ECommerceDbContext> options) : base(options) { }

    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Cart> Carts { get; set; } = null!;
    public DbSet<CartItem> CartItems { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<Review> Reviews { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Category>()
            .HasMany(c => c.Products)
            .WithOne(p => p.Category)
            .HasForeignKey(p => p.CategoryId);

        // One review per user per product
        modelBuilder.Entity<Review>()
            .HasIndex(r => new { r.ProductId, r.UserId })
            .IsUnique();

        // Fix Decimal warnings
        modelBuilder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);
            
        modelBuilder.Entity<CartItem>()
            .Property(c => c.UnitPrice)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Order>()
            .Property(o => o.TotalAmount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.UnitPrice)
            .HasPrecision(18, 2);

        // Seed data for Module 2 testing
        modelBuilder.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Electronics" },
            new Category { Id = 2, Name = "Accessories" }
        );

        modelBuilder.Entity<Product>().HasData(
            new Product { Id = 1, Name = "Gaming Laptop", Description = "High performance laptop", Price = 1200.00m, StockQuantity = 10, CategoryId = 1 },
            new Product { Id = 2, Name = "Wireless Mouse", Description = "Ergonomic mouse", Price = 25.50m, StockQuantity = 50, CategoryId = 2 }
        );
    }
}
