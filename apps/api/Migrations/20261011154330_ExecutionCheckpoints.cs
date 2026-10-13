using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Executions_Id_OrganizationId_ProjectId",
                table: "Executions",
                columns: new[] { "Id", "OrganizationId", "ProjectId" });

            migrationBuilder.CreateTable(
                name: "ExecutionCheckpoints",
                columns: table => new
                {
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Context = table.Column<string>(type: "jsonb", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionCheckpoints", x => new { x.ExecutionId, x.Attempt, x.Sequence });
                    table.ForeignKey(
                        name: "FK_ExecutionCheckpoints_Executions_ExecutionId_OrganizationId_~",
                        columns: x => new { x.ExecutionId, x.OrganizationId, x.ProjectId },
                        principalTable: "Executions",
                        principalColumns: new[] { "Id", "OrganizationId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionCheckpoints_ExecutionId_OrganizationId_ProjectId",
                table: "ExecutionCheckpoints",
                columns: new[] { "ExecutionId", "OrganizationId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionCheckpoints_OrganizationId_ProjectId_ExecutionId",
                table: "ExecutionCheckpoints",
                columns: new[] { "OrganizationId", "ProjectId", "ExecutionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionCheckpoints");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Executions_Id_OrganizationId_ProjectId",
                table: "Executions");
        }
    }
}
