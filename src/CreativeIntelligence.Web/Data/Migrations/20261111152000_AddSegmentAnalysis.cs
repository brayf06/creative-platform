using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSegmentAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnalysisError",
                table: "MediaAssets",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnalysisStatus",
                table: "MediaAssets",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiResultCaches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    CacheKey = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiResultCaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiResultCaches_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SegmentAnalyses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    MediaSegmentId = table.Column<long>(type: "bigint", nullable: false),
                    StartMs = table.Column<int>(type: "int", nullable: false),
                    EndMs = table.Column<int>(type: "int", nullable: false),
                    VisualDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OnScreenText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Product = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Actions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CommercialAngle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    MainMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Audience = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    VisualQuality = table.Column<int>(type: "int", nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(4,3)", precision: 4, scale: 3, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SegmentAnalyses_MediaSegments_MediaSegmentId",
                        column: x => x.MediaSegmentId,
                        principalTable: "MediaSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SegmentAnalyses_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SegmentAnalysisFunctions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    SegmentAnalysisId = table.Column<long>(type: "bigint", nullable: false),
                    Function = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentAnalysisFunctions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SegmentAnalysisFunctions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SegmentAnalysisFunctions_SegmentAnalyses_SegmentAnalysisId",
                        column: x => x.SegmentAnalysisId,
                        principalTable: "SegmentAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiResultCaches_OrganizationId_CacheKey",
                table: "AiResultCaches",
                columns: new[] { "OrganizationId", "CacheKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SegmentAnalyses_MediaSegmentId_IsCurrent",
                table: "SegmentAnalyses",
                columns: new[] { "MediaSegmentId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_SegmentAnalyses_OrganizationId",
                table: "SegmentAnalyses",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_SegmentAnalysisFunctions_OrganizationId_Function",
                table: "SegmentAnalysisFunctions",
                columns: new[] { "OrganizationId", "Function" });

            migrationBuilder.CreateIndex(
                name: "IX_SegmentAnalysisFunctions_SegmentAnalysisId_Function",
                table: "SegmentAnalysisFunctions",
                columns: new[] { "SegmentAnalysisId", "Function" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiResultCaches");

            migrationBuilder.DropTable(
                name: "SegmentAnalysisFunctions");

            migrationBuilder.DropTable(
                name: "SegmentAnalyses");

            migrationBuilder.DropColumn(
                name: "AnalysisError",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "AnalysisStatus",
                table: "MediaAssets");
        }
    }
}
