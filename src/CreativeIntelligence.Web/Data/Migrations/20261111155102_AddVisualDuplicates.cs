using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVisualDuplicates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DuplicateOfAssetId",
                table: "MediaAssets",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PerceptualHash",
                table: "MediaAssets",
                type: "varchar(80)",
                unicode: false,
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_DuplicateOfAssetId",
                table: "MediaAssets",
                column: "DuplicateOfAssetId");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaAssets_MediaAssets_DuplicateOfAssetId",
                table: "MediaAssets",
                column: "DuplicateOfAssetId",
                principalTable: "MediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaAssets_MediaAssets_DuplicateOfAssetId",
                table: "MediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_DuplicateOfAssetId",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "DuplicateOfAssetId",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "PerceptualHash",
                table: "MediaAssets");
        }
    }
}
