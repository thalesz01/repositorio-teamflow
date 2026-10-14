using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class TaskSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreviousTaskId",
                table: "WorkItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SequenceStartedBy",
                table: "WorkItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_PreviousTaskId",
                table: "WorkItems",
                column: "PreviousTaskId",
                unique: true,
                filter: "\"PreviousTaskId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_PreviousTaskId_OrganizationId_ProjectId",
                table: "WorkItems",
                columns: new[] { "PreviousTaskId", "OrganizationId", "ProjectId" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_WorkItems_PreviousTaskId_OrganizationId_ProjectId",
                table: "WorkItems",
                columns: new[] { "PreviousTaskId", "OrganizationId", "ProjectId" },
                principalTable: "WorkItems",
                principalColumns: new[] { "Id", "OrganizationId", "ProjectId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_WorkItems_PreviousTaskId_OrganizationId_ProjectId",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_PreviousTaskId",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_PreviousTaskId_OrganizationId_ProjectId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "PreviousTaskId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "SequenceStartedBy",
                table: "WorkItems");
        }
    }
}
