using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AgentExecutionRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResultCheckpointSequence",
                table: "Executions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultCode",
                table: "Executions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResultCreatedAt",
                table: "Executions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultHash",
                table: "Executions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResultOperationId",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ModelTurns",
                columns: table => new
                {
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReservedUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(20,12)", precision: 20, scale: 12, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelTurns", x => new { x.ExecutionId, x.OperationId });
                    table.ForeignKey(
                        name: "FK_ModelTurns_Executions_ExecutionId_OrganizationId_ProjectId",
                        columns: x => new { x.ExecutionId, x.OrganizationId, x.ProjectId },
                        principalTable: "Executions",
                        principalColumns: new[] { "Id", "OrganizationId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModelTurns_ExecutionId_Attempt_Sequence",
                table: "ModelTurns",
                columns: new[] { "ExecutionId", "Attempt", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelTurns_ExecutionId_OrganizationId_ProjectId",
                table: "ModelTurns",
                columns: new[] { "ExecutionId", "OrganizationId", "ProjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelTurns");

            migrationBuilder.DropColumn(
                name: "ResultCheckpointSequence",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ResultCode",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ResultCreatedAt",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ResultHash",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ResultOperationId",
                table: "Executions");
        }
    }
}
