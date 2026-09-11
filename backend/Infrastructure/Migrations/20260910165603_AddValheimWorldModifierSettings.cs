using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamePanel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddValheimWorldModifierSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ValheimWorldModifierSettings",
                columns: table => new
                {
                    ServerInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Combat = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ResourceRate = table.Column<double>(type: "REAL", nullable: false),
                    RaidRate = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DeathPenalty = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PortalMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PassiveEnemies = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlayerBasedRaids = table.Column<bool>(type: "INTEGER", nullable: false),
                    HammerMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    NoBuildCost = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValheimWorldModifierSettings", x => x.ServerInstanceId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ValheimWorldModifierSettings");
        }
    }
}
