namespace ECommerce.Api.Controllers;

using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    private readonly ECommerceDbContext _context;

    public AnalyticsController(ECommerceDbContext context)
    {
        _context = context;
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboardAnalytics([FromQuery] int days = 30)
    {
        var startDate = DateTime.UtcNow.Date.AddDays(-days);
        var previousStartDate = startDate.AddDays(-days);
        
        // --- 1. Fetch valid orders ---
        var allRelevantOrders = await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Category)
            .Where(o => o.Status != OrderStatus.Cancelled && o.OrderDate >= previousStartDate)
            .ToListAsync();

        var currentPeriodOrders = allRelevantOrders.Where(o => o.OrderDate >= startDate).ToList();
        var previousPeriodOrders = allRelevantOrders.Where(o => o.OrderDate >= previousStartDate && o.OrderDate < startDate).ToList();

        // --- 2. Top Metrics & Growth ---
        var totalRevenue = currentPeriodOrders.Sum(o => o.TotalAmount);
        var prevTotalRevenue = previousPeriodOrders.Sum(o => o.TotalAmount);
        var revenueGrowth = prevTotalRevenue > 0 ? ((totalRevenue - prevTotalRevenue) / prevTotalRevenue) * 100 : 0;

        var totalOrders = currentPeriodOrders.Count;
        var prevTotalOrders = previousPeriodOrders.Count;
        var ordersGrowth = prevTotalOrders > 0 ? ((decimal)(totalOrders - prevTotalOrders) / prevTotalOrders) * 100 : 0;

        var averageOrderValue = totalOrders > 0 ? totalRevenue / totalOrders : 0;
        var prevAov = prevTotalOrders > 0 ? prevTotalRevenue / prevTotalOrders : 0;
        var aovGrowth = prevAov > 0 ? ((averageOrderValue - prevAov) / prevAov) * 100 : 0;

        var totalCustomers = currentPeriodOrders.Select(o => o.CustomerEmail).Distinct().Count();
        var prevTotalCustomers = previousPeriodOrders.Select(o => o.CustomerEmail).Distinct().Count();
        var customersGrowth = prevTotalCustomers > 0 ? ((decimal)(totalCustomers - prevTotalCustomers) / prevTotalCustomers) * 100 : 0;

        // --- 3. Daily Sales (Current Period) ---
        var dailySales = currentPeriodOrders
            .GroupBy(o => o.OrderDate.Date)
            .Select(g => new {
                DateObj = g.Key,
                Revenue = g.Sum(o => o.TotalAmount)
            })
            .OrderBy(x => x.DateObj)
            .Select(x => new {
                Date = x.DateObj.ToString("MMM dd"),
                Revenue = x.Revenue
            })
            .ToList();

        var completeDailySales = Enumerable.Range(0, days)
            .Select(offset => startDate.AddDays(offset))
            .Select(date => new {
                Date = date.ToString("MMM dd"),
                Revenue = dailySales.FirstOrDefault(d => d.Date == date.ToString("MMM dd"))?.Revenue ?? 0m
            })
            .ToList();

        // --- 4. Category Sales (Donut Chart) ---
        var categorySales = currentPeriodOrders
            .SelectMany(o => o.Items)
            .Where(i => i.Product?.Category != null)
            .GroupBy(i => i.Product!.Category!.Name)
            .Select(g => new {
                Name = g.Key,
                Value = g.Sum(i => i.Quantity * i.UnitPrice)
            })
            .ToList();

        // --- 5. Detailed Top Products ---
        var topProducts = currentPeriodOrders
            .SelectMany(o => o.Items)
            .GroupBy(i => i.ProductId)
            .Select(g => new {
                Id = g.Key,
                Name = g.First().Product?.Name ?? "Unknown",
                Category = g.First().Product?.Category?.Name ?? "Unknown",
                ImageUrl = g.First().Product?.ImageUrl,
                Price = g.First().UnitPrice,
                Sales = g.Sum(i => i.Quantity),
                TotalRevenue = g.Sum(i => i.Quantity * i.UnitPrice)
            })
            .OrderByDescending(x => x.Sales)
            .Take(5)
            .ToList();

        // --- 6. Sales by Location ---
        var salesByLocation = currentPeriodOrders
            .Where(o => !string.IsNullOrEmpty(o.ShippingAddress))
            .GroupBy(o => {
                var parts = o.ShippingAddress.Split(',');
                if (parts.Length >= 1) return parts[parts.Length - 1].Trim();
                return o.ShippingAddress.Trim();
            })
            .Select(g => new {
                Location = g.Key,
                Revenue = g.Sum(o => o.TotalAmount)
            })
            .OrderByDescending(x => x.Revenue)
            .Take(5)
            .ToList();

        var totalLocationRevenue = salesByLocation.Sum(x => x.Revenue);
        var locationStats = salesByLocation.Select(x => new {
            Location = x.Location,
            Revenue = x.Revenue,
            Percentage = totalLocationRevenue > 0 ? Math.Round((double)(x.Revenue / totalLocationRevenue * 100), 1) : 0
        }).ToList();

        // --- 7. Recent Orders ---
        var recentOrders = await _context.Orders
            .OrderByDescending(o => o.OrderDate)
            .Take(5)
            .Select(o => new {
                o.Id,
                o.CustomerEmail,
                o.TotalAmount,
                Status = o.Status.ToString(),
                Date = o.OrderDate.ToString("MMM dd, yyyy HH:mm")
            })
            .ToListAsync();

        // --- 8. Low Stock Alerts ---
        var lowStockProducts = await _context.Products
            .Where(p => p.StockQuantity <= 15)
            .Select(p => new {
                p.Id,
                p.Name,
                p.ImageUrl,
                p.StockQuantity
            })
            .OrderBy(p => p.StockQuantity)
            .ToListAsync();

        return Ok(new
        {
            TotalRevenue = totalRevenue,
            RevenueGrowth = Math.Round((double)revenueGrowth, 1),
            TotalOrders = totalOrders,
            OrdersGrowth = Math.Round((double)ordersGrowth, 1),
            AverageOrderValue = averageOrderValue,
            AovGrowth = Math.Round((double)aovGrowth, 1),
            TotalCustomers = totalCustomers,
            CustomersGrowth = Math.Round((double)customersGrowth, 1),
            DailySales = completeDailySales,
            CategorySales = categorySales,
            TopProducts = topProducts,
            LocationSales = locationStats,
            RecentOrders = recentOrders,
            LowStockProducts = lowStockProducts
        });
    }
}
