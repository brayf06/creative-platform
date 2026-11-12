using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCreativePlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreativePlans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    CampaignBriefId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedCount = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PromptVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CostUsd = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreativePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreativePlans_CampaignBriefs_CampaignBriefId",
                        column: x => x.CampaignBriefId,
                        principalTable: "CampaignBriefs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreativePlans_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreativeConcepts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    CreativePlanId = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    Hypothesis = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Approach = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Hook = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Script = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Development = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Cta = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Audience = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequiredMaterial = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Risks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Novelty = table.Column<int>(type: "int", nullable: false),
                    MissingText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ViolationsText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreativeRecipeId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreativeConcepts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreativeConcepts_CreativePlans_CreativePlanId",
                        column: x => x.CreativePlanId,
                        principalTable: "CreativePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CreativeConcepts_CreativeRecipes_CreativeRecipeId",
                        column: x => x.CreativeRecipeId,
                        principalTable: "CreativeRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreativeConcepts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConceptFragments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    CreativeConceptId = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    MediaSegmentId = table.Column<long>(type: "bigint", nullable: true),
                    MediaAssetId = table.Column<long>(type: "bigint", nullable: false),
                    StartMs = table.Column<int>(type: "int", nullable: false),
                    EndMs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptFragments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConceptFragments_CreativeConcepts_CreativeConceptId",
                        column: x => x.CreativeConceptId,
                        principalTable: "CreativeConcepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConceptFragments_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConceptFragments_MediaSegments_MediaSegmentId",
                        column: x => x.MediaSegmentId,
                        principalTable: "MediaSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ConceptFragments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConceptFragments_CreativeConceptId_Position",
                table: "ConceptFragments",
                columns: new[] { "CreativeConceptId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConceptFragments_MediaAssetId",
                table: "ConceptFragments",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptFragments_MediaSegmentId",
                table: "ConceptFragments",
                column: "MediaSegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptFragments_OrganizationId",
                table: "ConceptFragments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CreativeConcepts_CreativePlanId_Position",
                table: "CreativeConcepts",
                columns: new[] { "CreativePlanId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreativeConcepts_CreativeRecipeId",
                table: "CreativeConcepts",
                column: "CreativeRecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_CreativeConcepts_OrganizationId",
                table: "CreativeConcepts",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CreativePlans_CampaignBriefId",
                table: "CreativePlans",
                column: "CampaignBriefId");

            migrationBuilder.CreateIndex(
                name: "IX_CreativePlans_OrganizationId",
                table: "CreativePlans",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConceptFragments");

            migrationBuilder.DropTable(
                name: "CreativeConcepts");

            migrationBuilder.DropTable(
                name: "CreativePlans");
        }
    }
}
