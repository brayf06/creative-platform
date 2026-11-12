using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionPlans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestedItems = table.Column<int>(type: "int", nullable: false),
                    WeightsVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionPlans_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionPlanItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    ProductionPlanId = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    HypothesisId = table.Column<long>(type: "bigint", nullable: false),
                    Statement = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Variable = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Component = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    MediaAssetId = table.Column<long>(type: "bigint", nullable: true),
                    StartMs = table.Column<int>(type: "int", nullable: true),
                    EndMs = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: false),
                    Justification = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    FactorsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LearningId = table.Column<long>(type: "bigint", nullable: true),
                    Recommendation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionPlanItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionPlanItems_Hypotheses_HypothesisId",
                        column: x => x.HypothesisId,
                        principalTable: "Hypotheses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionPlanItems_Learnings_LearningId",
                        column: x => x.LearningId,
                        principalTable: "Learnings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionPlanItems_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionPlanItems_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionPlanItems_ProductionPlans_ProductionPlanId",
                        column: x => x.ProductionPlanId,
                        principalTable: "ProductionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionPlanItems_HypothesisId",
                table: "ProductionPlanItems",
                column: "HypothesisId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionPlanItems_LearningId",
                table: "ProductionPlanItems",
                column: "LearningId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionPlanItems_MediaAssetId",
                table: "ProductionPlanItems",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionPlanItems_OrganizationId",
                table: "ProductionPlanItems",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionPlanItems_ProductionPlanId_HypothesisId",
                table: "ProductionPlanItems",
                columns: new[] { "ProductionPlanId", "HypothesisId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionPlanItems_ProductionPlanId_Position",
                table: "ProductionPlanItems",
                columns: new[] { "ProductionPlanId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionPlans_OrganizationId_CreatedAt",
                table: "ProductionPlans",
                columns: new[] { "OrganizationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionPlanItems");

            migrationBuilder.DropTable(
                name: "ProductionPlans");
        }
    }
}
