using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class PullRequestPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PublicationAttempts",
                table: "Executions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PublicationFailureCode",
                table: "Executions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublicationLease",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublicationLeaseUntil",
                table: "Executions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicationState",
                table: "Executions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishedHeadCommit",
                table: "Executions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PullRequestNumber",
                table: "Executions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PullRequestUrl",
                table: "Executions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublicationAttempts",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "PublicationFailureCode",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "PublicationLease",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "PublicationLeaseUntil",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "PublicationState",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "PublishedHeadCommit",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "PullRequestNumber",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "PullRequestUrl",
                table: "Executions");
        }
    }
}
