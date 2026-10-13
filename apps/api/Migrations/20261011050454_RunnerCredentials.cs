using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class RunnerCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RunnerCredentialId",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RunnerCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunnerCredentials_Projects_ProjectId_OrganizationId",
                        columns: x => new { x.ProjectId, x.OrganizationId },
                        principalTable: "Projects",
                        principalColumns: new[] { "Id", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Executions_RunnerCredentialId",
                table: "Executions",
                column: "RunnerCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerCredentials_ProjectId_OrganizationId",
                table: "RunnerCredentials",
                columns: new[] { "ProjectId", "OrganizationId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Executions_RunnerCredentials_RunnerCredentialId",
                table: "Executions",
                column: "RunnerCredentialId",
                principalTable: "RunnerCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Executions_RunnerCredentials_RunnerCredentialId",
                table: "Executions");

            migrationBuilder.DropTable(
                name: "RunnerCredentials");

            migrationBuilder.DropIndex(
                name: "IX_Executions_RunnerCredentialId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "RunnerCredentialId",
                table: "Executions");
        }
    }
}
