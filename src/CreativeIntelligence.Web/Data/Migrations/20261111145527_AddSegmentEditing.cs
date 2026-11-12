using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSegmentEditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SelectedForProduction",
                table: "MediaSegments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SegmentsVersion",
                table: "MediaAssets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MediaSegmentTags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    MediaSegmentId = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaSegmentTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaSegmentTags_MediaSegments_MediaSegmentId",
                        column: x => x.MediaSegmentId,
                        principalTable: "MediaSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaSegmentTags_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaSegmentTags_MediaSegmentId_Text",
                table: "MediaSegmentTags",
                columns: new[] { "MediaSegmentId", "Text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaSegmentTags_OrganizationId_Text",
                table: "MediaSegmentTags",
                columns: new[] { "OrganizationId", "Text" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaSegmentTags");

            migrationBuilder.DropColumn(
                name: "SelectedForProduction",
                table: "MediaSegments");

            migrationBuilder.DropColumn(
                name: "SegmentsVersion",
                table: "MediaAssets");
        }
    }
}
