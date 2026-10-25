using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoanApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicantName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MonthlyIncome = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ExistingMonthlyDebt = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    LoanAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AnnualInterestRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TenureMonths = table.Column<int>(type: "int", nullable: false),
                    EmploymentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanApplications", x => x.Id);
                    table.CheckConstraint("CK_LoanApplications_AnnualInterestRate_Range", "[AnnualInterestRate] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_LoanApplications_ExistingMonthlyDebt_NonNegative", "[ExistingMonthlyDebt] IS NULL OR [ExistingMonthlyDebt] >= 0");
                    table.CheckConstraint("CK_LoanApplications_LoanAmount_Positive", "[LoanAmount] > 0");
                    table.CheckConstraint("CK_LoanApplications_MonthlyIncome_NonNegative", "[MonthlyIncome] IS NULL OR [MonthlyIncome] >= 0");
                    table.CheckConstraint("CK_LoanApplications_TenureMonths_Range", "[TenureMonths] BETWEEN 1 AND 600");
                });

            migrationBuilder.CreateTable(
                name: "RuleAssessments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationId = table.Column<int>(type: "int", nullable: false),
                    Emi = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DebtToIncomeRatio = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    FlagsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleAssessments", x => x.Id);
                    table.CheckConstraint("CK_RuleAssessments_FlagsJson_IsJson", "ISJSON([FlagsJson]) = 1");
                    table.ForeignKey(
                        name: "FK_RuleAssessments_LoanApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "LoanApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoanApplications_Status_CreatedAt",
                table: "LoanApplications",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RuleAssessments_ApplicationId_CreatedAt",
                table: "RuleAssessments",
                columns: new[] { "ApplicationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuleAssessments");

            migrationBuilder.DropTable(
                name: "LoanApplications");
        }
    }
}
