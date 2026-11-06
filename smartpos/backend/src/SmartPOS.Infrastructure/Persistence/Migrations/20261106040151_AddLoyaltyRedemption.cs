using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyaltyRedemption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LoyaltyPointsRedeemed",
                table: "Sales",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "LoyaltyRedemptionAmount",
                table: "Sales",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sales_LoyaltyPointsRedeemed_NonNegative",
                table: "Sales",
                sql: "\"LoyaltyPointsRedeemed\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sales_LoyaltyRedemptionAmount_NonNegative",
                table: "Sales",
                sql: "\"LoyaltyRedemptionAmount\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Sales_LoyaltyPointsRedeemed_NonNegative",
                table: "Sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sales_LoyaltyRedemptionAmount_NonNegative",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "LoyaltyPointsRedeemed",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "LoyaltyRedemptionAmount",
                table: "Sales");
        }
    }
}
