using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class SharedMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemoryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PullRequestUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Commit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryItems", x => x.Id);
                    table.UniqueConstraint("AK_MemoryItems_Id_OrganizationId_ProjectId", x => new { x.Id, x.OrganizationId, x.ProjectId });
                    table.ForeignKey(
                        name: "FK_MemoryItems_Executions_ExecutionId_OrganizationId_ProjectId",
                        columns: x => new { x.ExecutionId, x.OrganizationId, x.ProjectId },
                        principalTable: "Executions",
                        principalColumns: new[] { "Id", "OrganizationId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MemoryItems_Projects_ProjectId_OrganizationId",
                        columns: x => new { x.ProjectId, x.OrganizationId },
                        principalTable: "Projects",
                        principalColumns: new[] { "Id", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MemoryRevisions",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryRevisions", x => new { x.ItemId, x.Version });
                    table.ForeignKey(
                        name: "FK_MemoryRevisions_MemoryItems_ItemId_OrganizationId_ProjectId",
                        columns: x => new { x.ItemId, x.OrganizationId, x.ProjectId },
                        principalTable: "MemoryItems",
                        principalColumns: new[] { "Id", "OrganizationId", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryItems_ExecutionId",
                table: "MemoryItems",
                column: "ExecutionId",
                unique: true,
                filter: "\"ExecutionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MemoryItems_ExecutionId_OrganizationId_ProjectId",
                table: "MemoryItems",
                columns: new[] { "ExecutionId", "OrganizationId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryItems_OrganizationId_ProjectId_Key",
                table: "MemoryItems",
                columns: new[] { "OrganizationId", "ProjectId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemoryItems_ProjectId_OrganizationId",
                table: "MemoryItems",
                columns: new[] { "ProjectId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryRevisions_ItemId_OrganizationId_ProjectId",
                table: "MemoryRevisions",
                columns: new[] { "ItemId", "OrganizationId", "ProjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemoryRevisions");

            migrationBuilder.DropTable(
                name: "MemoryItems");
        }
    }
}
