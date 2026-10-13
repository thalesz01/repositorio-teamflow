using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class PortableAgentTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MemoryContext",
                table: "Executions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferSnapshot",
                table: "Executions",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MemoryContext",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "TransferSnapshot",
                table: "Executions");
        }
    }
}
