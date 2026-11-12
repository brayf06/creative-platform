using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSegmentAudioLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AudioMeanDb",
                table: "MediaSegments",
                type: "decimal(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AudioPeakDb",
                table: "MediaSegments",
                type: "decimal(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioMeanDb",
                table: "MediaSegments");

            migrationBuilder.DropColumn(
                name: "AudioPeakDb",
                table: "MediaSegments");
        }
    }
}
