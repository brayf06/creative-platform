using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandsAndBriefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Brands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ValueProposition = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CommunicationStyle = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VisualIdentity = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Objectives = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CommercialRestrictions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MonthlyBudgetUsd = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Brands_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BrandItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    BrandId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MarginPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrandItems_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BrandItems_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignBriefs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    BrandId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OriginalText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    Occasion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Audience = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Objective = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Approaches = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    VideoCount = table.Column<int>(type: "int", nullable: true),
                    Platform = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UseExternalResearch = table.Column<bool>(type: "bit", nullable: false),
                    MaxGenerationBudgetUsd = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    Autonomy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignBriefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignBriefs_BrandItems_ProductId",
                        column: x => x.ProductId,
                        principalTable: "BrandItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CampaignBriefs_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CampaignBriefs_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrandItems_BrandId_Kind_Text",
                table: "BrandItems",
                columns: new[] { "BrandId", "Kind", "Text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrandItems_OrganizationId",
                table: "BrandItems",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Brands_OrganizationId_Name",
                table: "Brands",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignBriefs_BrandId",
                table: "CampaignBriefs",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignBriefs_OrganizationId_CreatedAt",
                table: "CampaignBriefs",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignBriefs_ProductId",
                table: "CampaignBriefs",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignBriefs");

            migrationBuilder.DropTable(
                name: "BrandItems");

            migrationBuilder.DropTable(
                name: "Brands");
        }
    }
}
