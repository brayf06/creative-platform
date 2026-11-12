using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreativeRecipes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Concept = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Hypothesis = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreativeRecipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreativeRecipes_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreativeVariants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    CreativeRecipeId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StoragePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreativeVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreativeVariants_CreativeRecipes_CreativeRecipeId",
                        column: x => x.CreativeRecipeId,
                        principalTable: "CreativeRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreativeVariants_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecipeItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    CreativeRecipeId = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    MediaAssetId = table.Column<long>(type: "bigint", nullable: false),
                    MediaSegmentId = table.Column<long>(type: "bigint", nullable: true),
                    StartMs = table.Column<int>(type: "int", nullable: false),
                    EndMs = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeItems", x => x.Id);
                    table.CheckConstraint("CK_RecipeItems_Range", "[EndMs] > [StartMs] AND [StartMs] >= 0");
                    table.ForeignKey(
                        name: "FK_RecipeItems_CreativeRecipes_CreativeRecipeId",
                        column: x => x.CreativeRecipeId,
                        principalTable: "CreativeRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeItems_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecipeItems_MediaSegments_MediaSegmentId",
                        column: x => x.MediaSegmentId,
                        principalTable: "MediaSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecipeItems_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QualityChecks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    CreativeVariantId = table.Column<long>(type: "bigint", nullable: false),
                    Rule = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualityChecks_CreativeVariants_CreativeVariantId",
                        column: x => x.CreativeVariantId,
                        principalTable: "CreativeVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QualityChecks_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreativeRecipes_OrganizationId_CreatedAt",
                table: "CreativeRecipes",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CreativeVariants_CreativeRecipeId",
                table: "CreativeVariants",
                column: "CreativeRecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_CreativeVariants_OrganizationId",
                table: "CreativeVariants",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityChecks_CreativeVariantId_Rule",
                table: "QualityChecks",
                columns: new[] { "CreativeVariantId", "Rule" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualityChecks_OrganizationId",
                table: "QualityChecks",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeItems_CreativeRecipeId_Position",
                table: "RecipeItems",
                columns: new[] { "CreativeRecipeId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecipeItems_MediaAssetId",
                table: "RecipeItems",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeItems_MediaSegmentId",
                table: "RecipeItems",
                column: "MediaSegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeItems_OrganizationId",
                table: "RecipeItems",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QualityChecks");

            migrationBuilder.DropTable(
                name: "RecipeItems");

            migrationBuilder.DropTable(
                name: "CreativeVariants");

            migrationBuilder.DropTable(
                name: "CreativeRecipes");
        }
    }
}
