using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnomalies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MaxBatchBudget",
                table: "AutomationPolicies",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "ProductionPlanId",
                table: "AutomationActions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AnomalyEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    MetaAdId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnomalyEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnomalyEvents_MetaAds_MetaAdId",
                        column: x => x.MetaAdId,
                        principalTable: "MetaAds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnomalyEvents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationActions_ProductionPlanId",
                table: "AutomationActions",
                column: "ProductionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyEvents_MetaAdId_Kind_Date",
                table: "AnomalyEvents",
                columns: new[] { "MetaAdId", "Kind", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyEvents_OrganizationId_ReviewedAt",
                table: "AnomalyEvents",
                columns: new[] { "OrganizationId", "ReviewedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_AutomationActions_ProductionPlans_ProductionPlanId",
                table: "AutomationActions",
                column: "ProductionPlanId",
                principalTable: "ProductionPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AutomationActions_ProductionPlans_ProductionPlanId",
                table: "AutomationActions");

            migrationBuilder.DropTable(
                name: "AnomalyEvents");

            migrationBuilder.DropIndex(
                name: "IX_AutomationActions_ProductionPlanId",
                table: "AutomationActions");

            migrationBuilder.DropColumn(
                name: "MaxBatchBudget",
                table: "AutomationPolicies");

            migrationBuilder.DropColumn(
                name: "ProductionPlanId",
                table: "AutomationActions");
        }
    }
}
