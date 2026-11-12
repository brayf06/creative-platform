using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTranscripts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TranscriptError",
                table: "MediaAssets",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TranscriptLanguage",
                table: "MediaAssets",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TranscriptModel",
                table: "MediaAssets",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TranscriptStatus",
                table: "MediaAssets",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaTranscriptLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    MediaAssetId = table.Column<long>(type: "bigint", nullable: false),
                    StartMs = table.Column<int>(type: "int", nullable: false),
                    EndMs = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaTranscriptLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaTranscriptLines_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaTranscriptLines_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaTranscriptLines_MediaAssetId_StartMs",
                table: "MediaTranscriptLines",
                columns: new[] { "MediaAssetId", "StartMs" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaTranscriptLines_OrganizationId",
                table: "MediaTranscriptLines",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaTranscriptLines");

            migrationBuilder.DropColumn(
                name: "TranscriptError",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "TranscriptLanguage",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "TranscriptModel",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "TranscriptStatus",
                table: "MediaAssets");
        }
    }
}
