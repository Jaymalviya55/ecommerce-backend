namespace ECommerce.Api.Models;

public sealed record Features
{
    // Admin Dashboard Features
    public static readonly Features AdminDashboard = new("@admin/dashboard");
    public static readonly Features AdminAnalytics = new("@admin/analytics");
    public static readonly Features AdminCoupons = new("@admin/coupons");
    public static readonly Features AdminOrders = new("@admin/orders");
    public static readonly Features AdminProducts = new("@admin/products");
    public static readonly Features AdminCategories = new("@admin/categories");
    public static readonly Features AdminUserManagement = new("@admin/user-management");

    // Warehouse & Support
    public static readonly Features Fulfillment = new("@fulfillment/orders");
    public static readonly Features Support = new("@support/desk");
    public static readonly Features SupportAnalytics = new("@analytics/view");

    private Features(string value)
    {
        Value = value;
    }

    public string Value { get; }
}
