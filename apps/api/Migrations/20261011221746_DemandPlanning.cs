using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class DemandPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AnalysisId",
                table: "WorkItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DemandAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceBundle = table.Column<string>(type: "jsonb", nullable: false),
                    Proposal = table.Column<string>(type: "jsonb", nullable: true),
                    MaxCostUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    ReservedUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(20,12)", precision: 20, scale: 12, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemandAnalyses", x => x.Id);
                    table.UniqueConstraint("AK_DemandAnalyses_Id_OrganizationId_ProjectId", x => new { x.Id, x.OrganizationId, x.ProjectId });
                    table.ForeignKey(
                        name: "FK_DemandAnalyses_Projects_ProjectId_OrganizationId",
                        columns: x => new { x.ProjectId, x.OrganizationId },
                        principalTable: "Projects",
                        principalColumns: new[] { "Id", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_AnalysisId_OrganizationId_ProjectId",
                table: "WorkItems",
                columns: new[] { "AnalysisId", "OrganizationId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_DemandAnalyses_OrganizationId_ProjectId_IdempotencyKey",
                table: "DemandAnalyses",
                columns: new[] { "OrganizationId", "ProjectId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemandAnalyses_ProjectId_OrganizationId",
                table: "DemandAnalyses",
                columns: new[] { "ProjectId", "OrganizationId" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_DemandAnalyses_AnalysisId_OrganizationId_ProjectId",
                table: "WorkItems",
                columns: new[] { "AnalysisId", "OrganizationId", "ProjectId" },
                principalTable: "DemandAnalyses",
                principalColumns: new[] { "Id", "OrganizationId", "ProjectId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_DemandAnalyses_AnalysisId_OrganizationId_ProjectId",
                table: "WorkItems");

            migrationBuilder.DropTable(
                name: "DemandAnalyses");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_AnalysisId_OrganizationId_ProjectId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "AnalysisId",
                table: "WorkItems");
        }
    }
}
