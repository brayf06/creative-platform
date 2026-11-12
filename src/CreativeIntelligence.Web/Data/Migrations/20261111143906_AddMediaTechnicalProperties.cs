using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaTechnicalProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudioCodec",
                table: "MediaAssets",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Container",
                table: "MediaAssets",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "MediaAssets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FrameRate",
                table: "MediaAssets",
                type: "decimal(7,3)",
                precision: 7,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "MediaAssets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingError",
                table: "MediaAssets",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingStatus",
                table: "MediaAssets",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                // Los archivos cargados antes de esta migración quedan pendientes de análisis.
                defaultValue: "Pending");

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailPath",
                table: "MediaAssets",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoCodec",
                table: "MediaAssets",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "MediaAssets",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioCodec",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "Container",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "FrameRate",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "ProcessingError",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "ProcessingStatus",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "ThumbnailPath",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "VideoCodec",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "MediaAssets");
        }
    }
}
