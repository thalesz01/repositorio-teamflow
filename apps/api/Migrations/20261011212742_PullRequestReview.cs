using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class PullRequestReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfirmedMergeCommit",
                table: "Executions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewApprovedBy",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewApprovedHead",
                table: "Executions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewCheckedAt",
                table: "Executions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReviewCiPassed",
                table: "Executions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReviewHeadCommit",
                table: "Executions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewState",
                table: "Executions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmedMergeCommit",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ReviewApprovedBy",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ReviewApprovedHead",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ReviewCheckedAt",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ReviewCiPassed",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ReviewHeadCommit",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ReviewState",
                table: "Executions");
        }
    }
}
