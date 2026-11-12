using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExperiments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Experiments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Hypothesis = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Variable = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MinImpressionsPerArm = table.Column<int>(type: "int", nullable: false),
                    Conclusion = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ConcludedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Experiments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Experiments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExperimentArms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    ExperimentId = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MetaAdId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperimentArms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExperimentArms_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExperimentArms_MetaAds_MetaAdId",
                        column: x => x.MetaAdId,
                        principalTable: "MetaAds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExperimentArms_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentArms_ExperimentId_MetaAdId",
                table: "ExperimentArms",
                columns: new[] { "ExperimentId", "MetaAdId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentArms_ExperimentId_Position",
                table: "ExperimentArms",
                columns: new[] { "ExperimentId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentArms_MetaAdId",
                table: "ExperimentArms",
                column: "MetaAdId");

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentArms_OrganizationId",
                table: "ExperimentArms",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Experiments_OrganizationId_CreatedAt",
                table: "Experiments",
                columns: new[] { "OrganizationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExperimentArms");

            migrationBuilder.DropTable(
                name: "Experiments");
        }
    }
}
