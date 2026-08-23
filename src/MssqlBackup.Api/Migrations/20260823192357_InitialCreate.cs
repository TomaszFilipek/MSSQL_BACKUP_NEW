using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MssqlBackup.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EnvironmentName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    InstanceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DatabaseName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BackupType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    OutputFilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    BackupDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Compress = table.Column<bool>(type: "INTEGER", nullable: false),
                    Verify = table.Column<bool>(type: "INTEGER", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_BackupDate",
                table: "BackupRecords",
                column: "BackupDate");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_EnvironmentName_DatabaseName",
                table: "BackupRecords",
                columns: new[] { "EnvironmentName", "DatabaseName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupRecords");
        }
    }
}
