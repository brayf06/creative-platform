using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPriorities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdRisk",
                table: "Hypotheses",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudienceRelevance",
                table: "Hypotheses",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PotentialMargin",
                table: "Hypotheses",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductionCost",
                table: "Hypotheses",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PriorityWeightSets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    HistoricalPerformance = table.Column<int>(type: "int", nullable: false),
                    EvidenceQuality = table.Column<int>(type: "int", nullable: false),
                    Similarity = table.Column<int>(type: "int", nullable: false),
                    Novelty = table.Column<int>(type: "int", nullable: false),
                    AudienceRelevance = table.Column<int>(type: "int", nullable: false),
                    MaterialAvailability = table.Column<int>(type: "int", nullable: false),
                    ProductionCost = table.Column<int>(type: "int", nullable: false),
                    AdRisk = table.Column<int>(type: "int", nullable: false),
                    Fatigue = table.Column<int>(type: "int", nullable: false),
                    PotentialMargin = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriorityWeightSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriorityWeightSets_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriorityWeightSets_OrganizationId_Version",
                table: "PriorityWeightSets",
                columns: new[] { "OrganizationId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriorityWeightSets");

            migrationBuilder.DropColumn(
                name: "AdRisk",
                table: "Hypotheses");

            migrationBuilder.DropColumn(
                name: "AudienceRelevance",
                table: "Hypotheses");

            migrationBuilder.DropColumn(
                name: "PotentialMargin",
                table: "Hypotheses");

            migrationBuilder.DropColumn(
                name: "ProductionCost",
                table: "Hypotheses");
        }
    }
}
