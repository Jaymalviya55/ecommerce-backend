namespace ECommerce.Infrastructure.Data;

using ECommerce.Domain.Entities;
using ECommerce.Domain.Entities.UserManagement;
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
    public DbSet<SupportTicket> SupportTickets { get; set; } = null!;
    public DbSet<TicketMessage> TicketMessages { get; set; } = null!;
    public DbSet<Coupon> Coupons { get; set; } = null!;
    public DbSet<Address> Addresses { get; set; } = null!;

    // User Management Domain DbSets
    public DbSet<UserType> UserTypes { get; set; } = null!;
    public DbSet<UserLevel> UserLevels { get; set; } = null!;
    public DbSet<UserRole> AppUserRoles { get; set; } = null!;
    public DbSet<UserLogin> AppUserLogins { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

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

        // Address to User relationship
        modelBuilder.Entity<Address>()
            .HasOne(a => a.User)
            .WithMany(u => u.Addresses)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // User Management Configurations
        modelBuilder.Entity<UserType>().HasKey(ut => ut.UserTypeId);
        modelBuilder.Entity<UserLevel>().HasKey(ul => ul.UserLevelId);
        modelBuilder.Entity<UserRole>().ToTable("UserRoles").HasKey(ur => ur.UserRoleId);
        modelBuilder.Entity<UserLogin>().ToTable("UserLogins").HasKey(ul => ul.UserId);
        modelBuilder.Entity<UserLogin>().HasIndex(ul => ul.UserName).IsUnique();

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
    }
}
