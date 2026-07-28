using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedCouponStatic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Coupons",
                columns: new[] { "Id", "Code", "CreatedAt", "DiscountPercentage", "ExpirationDate", "IsActive", "MinimumSpend" },
                values: new object[] { 1, "SUMMER20", new DateTime(2026, 7, 28, 0, 0, 0, 0, DateTimeKind.Utc), 20.00m, new DateTime(2027, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, 1000.00m });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Coupons",
                keyColumn: "Id",
                keyValue: 1);
        }
    }
}
