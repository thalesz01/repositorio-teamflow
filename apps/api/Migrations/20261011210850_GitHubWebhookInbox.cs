using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class GitHubWebhookInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_GitConnections_Id_OrganizationId_ProjectId",
                table: "GitConnections",
                columns: new[] { "Id", "OrganizationId", "ProjectId" });

            migrationBuilder.CreateTable(
                name: "GitHubDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Event = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitHubDeliveries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_GitConnections_Id_OrganizationId_ProjectId",
                table: "GitConnections");
        }
    }
}
