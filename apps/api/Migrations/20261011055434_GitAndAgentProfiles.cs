using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class GitAndAgentProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgentProfileId",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepositorySnapshot",
                table: "Executions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgentProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProtectedApiKey = table.Column<string>(type: "text", nullable: false),
                    MaxInputBytes = table.Column<int>(type: "integer", nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    InputUsdPerMillionTokens = table.Column<decimal>(type: "numeric(16,6)", precision: 16, scale: 6, nullable: false),
                    OutputUsdPerMillionTokens = table.Column<decimal>(type: "numeric(16,6)", precision: 16, scale: 6, nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentProfiles_Projects_ProjectId_OrganizationId",
                        columns: x => new { x.ProjectId, x.OrganizationId },
                        principalTable: "Projects",
                        principalColumns: new[] { "Id", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GitConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<long>(type: "bigint", nullable: false),
                    InstallationId = table.Column<long>(type: "bigint", nullable: false),
                    RepositoryId = table.Column<long>(type: "bigint", nullable: false),
                    Owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Repository = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BaseBranch = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BaseCommit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProtectedPrivateKey = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitConnections_Projects_ProjectId_OrganizationId",
                        columns: x => new { x.ProjectId, x.OrganizationId },
                        principalTable: "Projects",
                        principalColumns: new[] { "Id", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Executions_AgentProfileId",
                table: "Executions",
                column: "AgentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentProfiles_ProjectId_OrganizationId",
                table: "AgentProfiles",
                columns: new[] { "ProjectId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_GitConnections_OrganizationId_ProjectId",
                table: "GitConnections",
                columns: new[] { "OrganizationId", "ProjectId" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GitConnections_ProjectId_OrganizationId",
                table: "GitConnections",
                columns: new[] { "ProjectId", "OrganizationId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Executions_AgentProfiles_AgentProfileId",
                table: "Executions",
                column: "AgentProfileId",
                principalTable: "AgentProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Executions_AgentProfiles_AgentProfileId",
                table: "Executions");

            migrationBuilder.DropTable(
                name: "AgentProfiles");

            migrationBuilder.DropTable(
                name: "GitConnections");

            migrationBuilder.DropIndex(
                name: "IX_Executions_AgentProfileId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "AgentProfileId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "RepositorySnapshot",
                table: "Executions");
        }
    }
}
