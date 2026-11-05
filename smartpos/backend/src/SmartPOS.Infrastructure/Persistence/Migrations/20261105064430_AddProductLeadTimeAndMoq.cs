using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductLeadTimeAndMoq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LeadTimeDays",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "MinimumOrderQuantity",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_LeadTimeDays_Range",
                table: "Products",
                sql: "\"LeadTimeDays\" BETWEEN 1 AND 90");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_MinimumOrderQuantity_Positive",
                table: "Products",
                sql: "\"MinimumOrderQuantity\" >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_LeadTimeDays_Range",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_MinimumOrderQuantity_Positive",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LeadTimeDays",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "MinimumOrderQuantity",
                table: "Products");
        }
    }
}
