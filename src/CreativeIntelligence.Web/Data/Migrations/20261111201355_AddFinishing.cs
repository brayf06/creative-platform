using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MusicTrackId",
                table: "CreativeRecipes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MusicVolumePercent",
                table: "CreativeRecipes",
                type: "int",
                nullable: false,
                defaultValue: 25);

            migrationBuilder.AddColumn<bool>(
                name: "Subtitles",
                table: "CreativeRecipes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Transition",
                table: "CreativeRecipes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Cut");

            migrationBuilder.CreateTable(
                name: "MusicTracks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Sha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RightsHolder = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LicenseNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReuseAuthorized = table.Column<bool>(type: "bit", nullable: false),
                    RightsExpireOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MusicTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MusicTracks_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecipeOverlays",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    CreativeRecipeId = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Placement = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Moment = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeOverlays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeOverlays_CreativeRecipes_CreativeRecipeId",
                        column: x => x.CreativeRecipeId,
                        principalTable: "CreativeRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeOverlays_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreativeRecipes_MusicTrackId",
                table: "CreativeRecipes",
                column: "MusicTrackId");

            migrationBuilder.CreateIndex(
                name: "IX_MusicTracks_OrganizationId_Sha256",
                table: "MusicTracks",
                columns: new[] { "OrganizationId", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecipeOverlays_CreativeRecipeId_Position",
                table: "RecipeOverlays",
                columns: new[] { "CreativeRecipeId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecipeOverlays_OrganizationId",
                table: "RecipeOverlays",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreativeRecipes_MusicTracks_MusicTrackId",
                table: "CreativeRecipes",
                column: "MusicTrackId",
                principalTable: "MusicTracks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreativeRecipes_MusicTracks_MusicTrackId",
                table: "CreativeRecipes");

            migrationBuilder.DropTable(
                name: "MusicTracks");

            migrationBuilder.DropTable(
                name: "RecipeOverlays");

            migrationBuilder.DropIndex(
                name: "IX_CreativeRecipes_MusicTrackId",
                table: "CreativeRecipes");

            migrationBuilder.DropColumn(
                name: "MusicTrackId",
                table: "CreativeRecipes");

            migrationBuilder.DropColumn(
                name: "MusicVolumePercent",
                table: "CreativeRecipes");

            migrationBuilder.DropColumn(
                name: "Subtitles",
                table: "CreativeRecipes");

            migrationBuilder.DropColumn(
                name: "Transition",
                table: "CreativeRecipes");
        }
    }
}
