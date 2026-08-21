using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MssqlBackup.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EnvironmentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InstanceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BackupType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OutputFilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    BackupDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Compress = table.Column<bool>(type: "bit", nullable: false),
                    Verify = table.Column<bool>(type: "bit", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "time", nullable: false)
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
