using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSegmentEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SegmentEmbeddings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    MediaSegmentId = table.Column<long>(type: "bigint", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Dimensions = table.Column<int>(type: "int", nullable: false),
                    Vector = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    TextHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SegmentEmbeddings_MediaSegments_MediaSegmentId",
                        column: x => x.MediaSegmentId,
                        principalTable: "MediaSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SegmentEmbeddings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SegmentEmbeddings_MediaSegmentId_Model",
                table: "SegmentEmbeddings",
                columns: new[] { "MediaSegmentId", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SegmentEmbeddings_OrganizationId_Model",
                table: "SegmentEmbeddings",
                columns: new[] { "OrganizationId", "Model" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SegmentEmbeddings");
        }
    }
}
