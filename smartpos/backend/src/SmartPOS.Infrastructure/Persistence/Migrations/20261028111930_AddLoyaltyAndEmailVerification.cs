using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyaltyAndEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerificationExpiresAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailVerificationTokenHash",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LoyaltyPointsEarned",
                table: "Sales",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sales_LoyaltyPointsEarned_NonNegative",
                table: "Sales",
                sql: "\"LoyaltyPointsEarned\" >= 0");

            // One-time backfill for bills that already exist, using the default earning rule (1 point per 100 spent,
            // rounded down, completed bills with a customer only). From now on SaleService sets the points per bill.
            migrationBuilder.Sql("""
                UPDATE "Sales" SET "LoyaltyPointsEarned" = FLOOR("TotalAmount" / 100)::integer
                WHERE "CustomerId" IS NOT NULL AND "Status" = 'Completed';
                """);
            migrationBuilder.Sql("""
                UPDATE "Customers" c SET "LoyaltyPoints" =
                    COALESCE((SELECT SUM(s."LoyaltyPointsEarned") FROM "Sales" s WHERE s."CustomerId" = c."Id"), 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Sales_LoyaltyPointsEarned_NonNegative",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "EmailVerificationExpiresAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationTokenHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LoyaltyPointsEarned",
                table: "Sales");
        }
    }
}
