using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamePanel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLogAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LogAggregates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WindowEndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FatalCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerJoinCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerLeaveCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastErrorMessage = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogAggregates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LogAggregates_ServerInstanceId_WindowStartUtc",
                table: "LogAggregates",
                columns: new[] { "ServerInstanceId", "WindowStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogAggregates_WindowStartUtc",
                table: "LogAggregates",
                column: "WindowStartUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogAggregates");
        }
    }
}
