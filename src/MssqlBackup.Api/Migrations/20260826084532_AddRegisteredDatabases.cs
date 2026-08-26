using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MssqlBackup.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRegisteredDatabases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegisteredDatabases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EnvironmentName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    InstanceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DatabaseName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DatabaseKey = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegisteredDatabases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredDatabases_DatabaseKey",
                table: "RegisteredDatabases",
                column: "DatabaseKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredDatabases_EnvironmentName_InstanceName",
                table: "RegisteredDatabases",
                columns: new[] { "EnvironmentName", "InstanceName" });

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredDatabases_IsActive",
                table: "RegisteredDatabases",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredDatabases_LastSeenAt",
                table: "RegisteredDatabases",
                column: "LastSeenAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegisteredDatabases");
        }
    }
}
