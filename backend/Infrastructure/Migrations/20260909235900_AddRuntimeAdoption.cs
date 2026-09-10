using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamePanel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeAdoption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackupPath",
                table: "ServerInstances",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataPath",
                table: "ServerInstances",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstallationPath",
                table: "ServerInstances",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstanceKey",
                table: "ServerInstances",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Protocol",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProvisioningMode",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Ready",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReadinessMarker",
                table: "ServerInstances",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuntimeId",
                table: "ServerInstances",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RuntimeType",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SteamAppId",
                table: "ServerInstances",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_ServerInstances_InstanceKey",
                table: "ServerInstances",
                column: "InstanceKey",
                unique: true,
                filter: "\"InstanceKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ServerInstances_RuntimeType_RuntimeId",
                table: "ServerInstances",
                columns: new[] { "RuntimeType", "RuntimeId" },
                unique: true,
                filter: "\"RuntimeId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ServerInstances_InstanceKey",
                table: "ServerInstances");

            migrationBuilder.DropIndex(
                name: "UX_ServerInstances_RuntimeType_RuntimeId",
                table: "ServerInstances");

            migrationBuilder.DropColumn(name: "BackupPath", table: "ServerInstances");
            migrationBuilder.DropColumn(name: "DataPath", table: "ServerInstances");
            migrationBuilder.DropColumn(name: "InstallationPath", table: "ServerInstances");
            migrationBuilder.DropColumn(name: "InstanceKey", table: "ServerInstances");
            migrationBuilder.DropColumn(name: "Protocol", table: "ServerInstances");
            migrationBuilder.DropColumn(name: "ProvisioningMode", table: "ServerInstances");
            migrationBuilder.DropColumn(name: "Ready", table: "ServerInstances");
            migrationBuilder.DropColumn(name: "ReadinessMarker", table: "ServerInstances");
            migrationBuilder.DropColumn(name: "RuntimeId", table: "ServerInstances");
            migrationBuilder.DropColumn(name: "RuntimeType", table: "ServerInstances");
            migrationBuilder.DropColumn(name: "SteamAppId", table: "ServerInstances");
        }
    }
}
