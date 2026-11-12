using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMetaPerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetaAdAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaAdAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetaAdAccounts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MetaImports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DefaultCurrency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    Skipped = table.Column<int>(type: "int", nullable: false),
                    Ads = table.Column<int>(type: "int", nullable: false),
                    FirstDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetaImports_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MetaCampaigns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    MetaAdAccountId = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Objective = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaCampaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetaCampaigns_MetaAdAccounts_MetaAdAccountId",
                        column: x => x.MetaAdAccountId,
                        principalTable: "MetaAdAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MetaCampaigns_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MetaAdSets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    MetaCampaignId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaAdSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetaAdSets_MetaCampaigns_MetaCampaignId",
                        column: x => x.MetaCampaignId,
                        principalTable: "MetaCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MetaAdSets_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MetaAds",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    MetaAdSetId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreativeVariantId = table.Column<long>(type: "bigint", nullable: true),
                    LinkOrigin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LinkedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaAds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetaAds_CreativeVariants_CreativeVariantId",
                        column: x => x.CreativeVariantId,
                        principalTable: "CreativeVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MetaAds_MetaAdSets_MetaAdSetId",
                        column: x => x.MetaAdSetId,
                        principalTable: "MetaAdSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MetaAds_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MetaInsightsDaily",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    MetaAdId = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Spend = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    Impressions = table.Column<long>(type: "bigint", nullable: false),
                    Clicks = table.Column<long>(type: "bigint", nullable: false),
                    VideoPlays = table.Column<long>(type: "bigint", nullable: true),
                    ThruPlays = table.Column<long>(type: "bigint", nullable: true),
                    Purchases = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    PurchaseValue = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaInsightsDaily", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetaInsightsDaily_MetaAds_MetaAdId",
                        column: x => x.MetaAdId,
                        principalTable: "MetaAds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MetaInsightsDaily_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetaAdAccounts_OrganizationId_ExternalId",
                table: "MetaAdAccounts",
                columns: new[] { "OrganizationId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetaAds_CreativeVariantId",
                table: "MetaAds",
                column: "CreativeVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_MetaAds_MetaAdSetId",
                table: "MetaAds",
                column: "MetaAdSetId");

            migrationBuilder.CreateIndex(
                name: "IX_MetaAds_OrganizationId_ExternalId",
                table: "MetaAds",
                columns: new[] { "OrganizationId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetaAdSets_MetaCampaignId_ExternalId",
                table: "MetaAdSets",
                columns: new[] { "MetaCampaignId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetaAdSets_OrganizationId",
                table: "MetaAdSets",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_MetaCampaigns_MetaAdAccountId_ExternalId",
                table: "MetaCampaigns",
                columns: new[] { "MetaAdAccountId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetaCampaigns_OrganizationId",
                table: "MetaCampaigns",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_MetaImports_OrganizationId_CreatedAt",
                table: "MetaImports",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MetaInsightsDaily_MetaAdId_Date",
                table: "MetaInsightsDaily",
                columns: new[] { "MetaAdId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetaInsightsDaily_OrganizationId_Date",
                table: "MetaInsightsDaily",
                columns: new[] { "OrganizationId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetaImports");

            migrationBuilder.DropTable(
                name: "MetaInsightsDaily");

            migrationBuilder.DropTable(
                name: "MetaAds");

            migrationBuilder.DropTable(
                name: "MetaAdSets");

            migrationBuilder.DropTable(
                name: "MetaCampaigns");

            migrationBuilder.DropTable(
                name: "MetaAdAccounts");
        }
    }
}
