using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class LocalCodexIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingMode",
                table: "ModelTurns",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "api");

            migrationBuilder.AddColumn<string>(
                name: "LocalPath",
                table: "GitConnections",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "GitConnections",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "github");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "AgentProfiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "openai");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingMode",
                table: "ModelTurns");

            migrationBuilder.DropColumn(
                name: "LocalPath",
                table: "GitConnections");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "GitConnections");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "AgentProfiles");
        }
    }
}
