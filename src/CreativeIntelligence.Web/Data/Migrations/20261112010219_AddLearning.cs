using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreativeIntelligence.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "HypothesisId",
                table: "Experiments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Hypotheses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Statement = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Variable = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PatternDimension = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PatternKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PatternLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PatternReading = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PatternCampaignId = table.Column<long>(type: "bigint", nullable: true),
                    PatternFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PatternTo = table.Column<DateOnly>(type: "date", nullable: true),
                    DiscardedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DiscardReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hypotheses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hypotheses_MetaCampaigns_PatternCampaignId",
                        column: x => x.PatternCampaignId,
                        principalTable: "MetaCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hypotheses_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Learnings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    HypothesisId = table.Column<long>(type: "bigint", nullable: true),
                    ExperimentId = table.Column<long>(type: "bigint", nullable: true),
                    Statement = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DataUsed = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Confidence = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConfidenceReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    Recommendation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Applicability = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CriteriaVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StatusChangedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplacesLearningId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Learnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Learnings_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Learnings_Hypotheses_HypothesisId",
                        column: x => x.HypothesisId,
                        principalTable: "Hypotheses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Learnings_Learnings_ReplacesLearningId",
                        column: x => x.ReplacesLearningId,
                        principalTable: "Learnings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Learnings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LearningEvidence",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    LearningId = table.Column<long>(type: "bigint", nullable: false),
                    MetaAdId = table.Column<long>(type: "bigint", nullable: true),
                    CreativeVariantId = table.Column<long>(type: "bigint", nullable: true),
                    MediaAssetId = table.Column<long>(type: "bigint", nullable: true),
                    StartMs = table.Column<int>(type: "int", nullable: true),
                    EndMs = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningEvidence_CreativeVariants_CreativeVariantId",
                        column: x => x.CreativeVariantId,
                        principalTable: "CreativeVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearningEvidence_Learnings_LearningId",
                        column: x => x.LearningId,
                        principalTable: "Learnings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LearningEvidence_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearningEvidence_MetaAds_MetaAdId",
                        column: x => x.MetaAdId,
                        principalTable: "MetaAds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearningEvidence_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Experiments_HypothesisId",
                table: "Experiments",
                column: "HypothesisId");

            migrationBuilder.CreateIndex(
                name: "IX_Hypotheses_OrganizationId_CreatedAt",
                table: "Hypotheses",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Hypotheses_PatternCampaignId",
                table: "Hypotheses",
                column: "PatternCampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningEvidence_CreativeVariantId",
                table: "LearningEvidence",
                column: "CreativeVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningEvidence_LearningId",
                table: "LearningEvidence",
                column: "LearningId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningEvidence_MediaAssetId",
                table: "LearningEvidence",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningEvidence_MetaAdId",
                table: "LearningEvidence",
                column: "MetaAdId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningEvidence_OrganizationId",
                table: "LearningEvidence",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Learnings_ExperimentId",
                table: "Learnings",
                column: "ExperimentId");

            migrationBuilder.CreateIndex(
                name: "IX_Learnings_HypothesisId",
                table: "Learnings",
                column: "HypothesisId");

            migrationBuilder.CreateIndex(
                name: "IX_Learnings_OrganizationId_Status_CreatedAt",
                table: "Learnings",
                columns: new[] { "OrganizationId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Learnings_ReplacesLearningId",
                table: "Learnings",
                column: "ReplacesLearningId");

            migrationBuilder.AddForeignKey(
                name: "FK_Experiments_Hypotheses_HypothesisId",
                table: "Experiments",
                column: "HypothesisId",
                principalTable: "Hypotheses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Experiments_Hypotheses_HypothesisId",
                table: "Experiments");

            migrationBuilder.DropTable(
                name: "LearningEvidence");

            migrationBuilder.DropTable(
                name: "Learnings");

            migrationBuilder.DropTable(
                name: "Hypotheses");

            migrationBuilder.DropIndex(
                name: "IX_Experiments_HypothesisId",
                table: "Experiments");

            migrationBuilder.DropColumn(
                name: "HypothesisId",
                table: "Experiments");
        }
    }
}
