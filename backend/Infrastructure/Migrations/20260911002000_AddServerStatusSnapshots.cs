using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamePanel.Infrastructure.Migrations;

[Migration("20260911002000_AddServerStatusSnapshots")]

public partial class AddServerStatusSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ServerStatusSnapshots",
            columns: table => new
            {
                ServerInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                ProcessId = table.Column<int>(type: "INTEGER", nullable: true),
                Ready = table.Column<bool>(type: "INTEGER", nullable: false),
                ObservedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ServerStatusSnapshots", x => x.ServerInstanceId);
                table.ForeignKey("FK_ServerStatusSnapshots_ServerInstances_ServerInstanceId", x => x.ServerInstanceId, "ServerInstances", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_ServerStatusSnapshots_ObservedAtUtc", "ServerStatusSnapshots", "ObservedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("ServerStatusSnapshots");
}
