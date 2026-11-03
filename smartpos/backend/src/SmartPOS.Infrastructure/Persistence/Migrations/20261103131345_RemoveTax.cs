using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tax is no longer charged. Recompute every existing bill as if it never had been, while the tax columns
            // still exist, so the history stays consistent (line totals add up to the bill, total = subtotal - discount).
            migrationBuilder.Sql("""
                UPDATE "SaleItems" SET "LineTotal" = "LineTotal" - "TaxAmount";
                """);
            // Exact payments (no change) now pay the new total. Cash handed over with change keeps the amount
            // handed over, so the change grows by the tax removed. PostgreSQL evaluates every SET against the old row.
            migrationBuilder.Sql("""
                UPDATE "Sales" SET
                    "AmountPaid" = CASE WHEN "ChangeAmount" = 0 THEN "TotalAmount" - "TaxAmount" ELSE "AmountPaid" END,
                    "ChangeAmount" = CASE WHEN "ChangeAmount" = 0 THEN 0 ELSE "AmountPaid" - ("TotalAmount" - "TaxAmount") END,
                    "TotalAmount" = "TotalAmount" - "TaxAmount";
                """);
            // Loyalty points follow the new totals, with the default earning rule (1 point per 100 spent, rounded down,
            // completed bills with a customer only), the same rule the loyalty migration used. Balances are re-derived.
            migrationBuilder.Sql("""
                UPDATE "Sales" SET "LoyaltyPointsEarned" = FLOOR("TotalAmount" / 100)::integer
                WHERE "CustomerId" IS NOT NULL AND "Status" = 'Completed';
                """);
            migrationBuilder.Sql("""
                UPDATE "Customers" c SET "LoyaltyPoints" =
                    COALESCE((SELECT SUM(s."LoyaltyPointsEarned") FROM "Sales" s WHERE s."CustomerId" = c."Id"), 0);
                """);

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "SaleItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Brings the columns back (as 0). The tax that old bills were charged is not restored: restore a backup for that.
            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "Sales",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRatePercent",
                table: "Sales",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "SaleItems",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
