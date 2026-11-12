using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RenderMs",
                table: "CreativeVariants",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CreativeBatchId",
                table: "CreativeRecipes",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CreativeBatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Brief = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    RequestedCount = table.Column<int>(type: "int", nullable: false),
                    MaxDurationMs = table.Column<int>(type: "int", nullable: false),
                    SimilarityThreshold = table.Column<decimal>(type: "decimal(4,3)", precision: 4, scale: 3, nullable: false),
                    Seed = table.Column<int>(type: "int", nullable: false),
                    DiscardedBySimilarity = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreativeBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreativeBatches_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreativeRecipes_CreativeBatchId",
                table: "CreativeRecipes",
                column: "CreativeBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_CreativeBatches_OrganizationId_CreatedAt",
                table: "CreativeBatches",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_CreativeRecipes_CreativeBatches_CreativeBatchId",
                table: "CreativeRecipes",
                column: "CreativeBatchId",
                principalTable: "CreativeBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreativeRecipes_CreativeBatches_CreativeBatchId",
                table: "CreativeRecipes");

            migrationBuilder.DropTable(
                name: "CreativeBatches");

            migrationBuilder.DropIndex(
                name: "IX_CreativeRecipes_CreativeBatchId",
                table: "CreativeRecipes");

            migrationBuilder.DropColumn(
                name: "RenderMs",
                table: "CreativeVariants");

            migrationBuilder.DropColumn(
                name: "CreativeBatchId",
                table: "CreativeRecipes");
        }
    }
}
