using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class MemoryIndexing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_AgentProfiles_Id_OrganizationId_ProjectId",
                table: "AgentProfiles",
                columns: new[] { "Id", "OrganizationId", "ProjectId" });

            migrationBuilder.CreateTable(
                name: "MemoryIndexes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    Chunker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Lease = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaxCostUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    ReservedUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(20,12)", precision: 20, scale: 12, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryIndexes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemoryIndexes_AgentProfiles_ProfileId_OrganizationId_Projec~",
                        columns: x => new { x.ProfileId, x.OrganizationId, x.ProjectId },
                        principalTable: "AgentProfiles",
                        principalColumns: new[] { "Id", "OrganizationId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MemoryIndexes_MemoryItems_ItemId_OrganizationId_ProjectId",
                        columns: x => new { x.ItemId, x.OrganizationId, x.ProjectId },
                        principalTable: "MemoryItems",
                        principalColumns: new[] { "Id", "OrganizationId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MemoryChunks",
                columns: table => new
                {
                    IndexId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Embedding = table.Column<float[]>(type: "real[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryChunks", x => new { x.IndexId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_MemoryChunks_MemoryIndexes_IndexId",
                        column: x => x.IndexId,
                        principalTable: "MemoryIndexes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryIndexes_ItemId_OrganizationId_ProjectId",
                table: "MemoryIndexes",
                columns: new[] { "ItemId", "OrganizationId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryIndexes_OrganizationId_ProjectId_ItemId_IdempotencyKey",
                table: "MemoryIndexes",
                columns: new[] { "OrganizationId", "ProjectId", "ItemId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemoryIndexes_ProfileId_OrganizationId_ProjectId",
                table: "MemoryIndexes",
                columns: new[] { "ProfileId", "OrganizationId", "ProjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemoryChunks");

            migrationBuilder.DropTable(
                name: "MemoryIndexes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AgentProfiles_Id_OrganizationId_ProjectId",
                table: "AgentProfiles");
        }
    }
}
