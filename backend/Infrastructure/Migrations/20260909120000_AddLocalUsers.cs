using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamePanel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedUsername = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            // Case-insensitive unique na NormalizedUsername. NormalizedUsername jest
            // przechowywany jako lowercase, więc BINARY unique wystarcza — ale dla
            // pewności używamy COLLATE NOCASE, by nazwa użytkownika była niejednoznaczna
            // nawet przy różnej wielkości liter.
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"UX_Users_NormalizedUsername\" ON \"Users\" (\"NormalizedUsername\" COLLATE NOCASE);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex("UX_Users_NormalizedUsername", "Users");
            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}