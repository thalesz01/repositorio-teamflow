using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class TaskFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GitConnections_OrganizationId_ProjectId",
                table: "GitConnections");

            migrationBuilder.AddColumn<string>(
                name: "BaseBranch",
                table: "WorkItems",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FolderPath",
                table: "WorkItems",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RepositoryConnectionId",
                table: "WorkItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ForTasks",
                table: "GitConnections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "ReviewFeedback",
                table: "Executions",
                type: "character varying(6000)",
                maxLength: 6000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_RepositoryConnectionId_OrganizationId_ProjectId",
                table: "WorkItems",
                columns: new[] { "RepositoryConnectionId", "OrganizationId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_GitConnections_OrganizationId_ProjectId",
                table: "GitConnections",
                columns: new[] { "OrganizationId", "ProjectId" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL AND NOT \"ForTasks\"");

            migrationBuilder.CreateIndex(
                name: "IX_GitConnections_OrganizationId_ProjectId_LocalPath_BaseBranch",
                table: "GitConnections",
                columns: new[] { "OrganizationId", "ProjectId", "LocalPath", "BaseBranch" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL AND \"ForTasks\"");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_GitConnections_RepositoryConnectionId_Organizatio~",
                table: "WorkItems",
                columns: new[] { "RepositoryConnectionId", "OrganizationId", "ProjectId" },
                principalTable: "GitConnections",
                principalColumns: new[] { "Id", "OrganizationId", "ProjectId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_GitConnections_RepositoryConnectionId_Organizatio~",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_RepositoryConnectionId_OrganizationId_ProjectId",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_GitConnections_OrganizationId_ProjectId",
                table: "GitConnections");

            migrationBuilder.DropIndex(
                name: "IX_GitConnections_OrganizationId_ProjectId_LocalPath_BaseBranch",
                table: "GitConnections");

            migrationBuilder.DropColumn(
                name: "BaseBranch",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "FolderPath",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "RepositoryConnectionId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ForTasks",
                table: "GitConnections");

            migrationBuilder.AlterColumn<string>(
                name: "ReviewFeedback",
                table: "Executions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(6000)",
                oldMaxLength: 6000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitConnections_OrganizationId_ProjectId",
                table: "GitConnections",
                columns: new[] { "OrganizationId", "ProjectId" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");
        }
    }
}
