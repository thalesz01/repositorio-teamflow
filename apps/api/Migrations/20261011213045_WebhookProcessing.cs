using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class WebhookProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "GitHubDeliveries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                table: "GitHubDeliveries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Lease",
                table: "GitHubDeliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseUntil",
                table: "GitHubDeliveries",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "GitHubDeliveries");

            migrationBuilder.DropColumn(
                name: "FailureCode",
                table: "GitHubDeliveries");

            migrationBuilder.DropColumn(
                name: "Lease",
                table: "GitHubDeliveries");

            migrationBuilder.DropColumn(
                name: "LeaseUntil",
                table: "GitHubDeliveries");
        }
    }
}
