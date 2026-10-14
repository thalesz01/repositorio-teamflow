using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class TaskAgentSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgentProfileId",
                table: "WorkItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTaskSelection",
                table: "AgentProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReasoningEffort",
                table: "AgentProfiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_AgentProfileId_OrganizationId_ProjectId",
                table: "WorkItems",
                columns: new[] { "AgentProfileId", "OrganizationId", "ProjectId" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_AgentProfiles_AgentProfileId_OrganizationId_Proje~",
                table: "WorkItems",
                columns: new[] { "AgentProfileId", "OrganizationId", "ProjectId" },
                principalTable: "AgentProfiles",
                principalColumns: new[] { "Id", "OrganizationId", "ProjectId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_AgentProfiles_AgentProfileId_OrganizationId_Proje~",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_AgentProfileId_OrganizationId_ProjectId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "AgentProfileId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "IsTaskSelection",
                table: "AgentProfiles");

            migrationBuilder.DropColumn(
                name: "ReasoningEffort",
                table: "AgentProfiles");
        }
    }
}
