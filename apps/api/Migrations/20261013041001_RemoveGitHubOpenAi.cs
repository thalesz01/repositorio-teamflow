using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGitHubOpenAi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0008: GitHub App and OpenAI API integrations are removed. Revoke what can no longer work
            // before its credentials and identifiers are dropped; executions keep their historical snapshots.
            migrationBuilder.Sql("""
                UPDATE "GitConnections" SET "RevokedAt" = now(), "Version" = gen_random_uuid()
                WHERE "Provider" <> 'local' AND "RevokedAt" IS NULL;
                UPDATE "AgentProfiles" SET "RevokedAt" = now()
                WHERE "Provider" <> 'codex' AND "RevokedAt" IS NULL;
                """);

            migrationBuilder.DropTable(
                name: "GitHubDeliveries");

            migrationBuilder.DropColumn(
                name: "AppId",
                table: "GitConnections");

            migrationBuilder.DropColumn(
                name: "InstallationId",
                table: "GitConnections");

            migrationBuilder.DropColumn(
                name: "Owner",
                table: "GitConnections");

            migrationBuilder.DropColumn(
                name: "ProtectedPrivateKey",
                table: "GitConnections");

            migrationBuilder.DropColumn(
                name: "Repository",
                table: "GitConnections");

            migrationBuilder.DropColumn(
                name: "ProtectedApiKey",
                table: "AgentProfiles");

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                table: "GitConnections",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "local",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "github");

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                table: "AgentProfiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "codex",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "openai");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                table: "GitConnections",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "github",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "local");

            migrationBuilder.AddColumn<long>(
                name: "AppId",
                table: "GitConnections",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "InstallationId",
                table: "GitConnections",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Owner",
                table: "GitConnections",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProtectedPrivateKey",
                table: "GitConnections",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Repository",
                table: "GitConnections",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                table: "AgentProfiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "openai",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "codex");

            migrationBuilder.AddColumn<string>(
                name: "ProtectedApiKey",
                table: "AgentProfiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "GitHubDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Event = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Lease = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubDeliveries_GitConnections_ConnectionId_OrganizationId~",
                        columns: x => new { x.ConnectionId, x.OrganizationId, x.ProjectId },
                        principalTable: "GitConnections",
                        principalColumns: new[] { "Id", "OrganizationId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubDeliveries_ConnectionId_DeliveryId",
                table: "GitHubDeliveries",
                columns: new[] { "ConnectionId", "DeliveryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitHubDeliveries_ConnectionId_OrganizationId_ProjectId",
                table: "GitHubDeliveries",
                columns: new[] { "ConnectionId", "OrganizationId", "ProjectId" });
        }
    }
}
