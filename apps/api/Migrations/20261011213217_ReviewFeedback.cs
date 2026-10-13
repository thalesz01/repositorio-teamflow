using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReviewFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentExecutionId",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewFeedback",
                table: "Executions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Executions_ParentExecutionId_OrganizationId_ProjectId",
                table: "Executions",
                columns: new[] { "ParentExecutionId", "OrganizationId", "ProjectId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Executions_Executions_ParentExecutionId_OrganizationId_Proj~",
                table: "Executions",
                columns: new[] { "ParentExecutionId", "OrganizationId", "ProjectId" },
                principalTable: "Executions",
                principalColumns: new[] { "Id", "OrganizationId", "ProjectId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Executions_Executions_ParentExecutionId_OrganizationId_Proj~",
                table: "Executions");

            migrationBuilder.DropIndex(
                name: "IX_Executions_ParentExecutionId_OrganizationId_ProjectId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ParentExecutionId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ReviewFeedback",
                table: "Executions");
        }
    }
}
