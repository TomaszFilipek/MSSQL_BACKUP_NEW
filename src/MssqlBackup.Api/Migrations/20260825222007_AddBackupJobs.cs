using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MssqlBackup.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    InstanceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    HostName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalDatabases = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentDatabase = table.Column<string>(type: "TEXT", nullable: true),
                    CurrentStep = table.Column<string>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_EnvironmentName_InstanceName",
                table: "BackupJobs",
                columns: new[] { "EnvironmentName", "InstanceName" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_Status",
                table: "BackupJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_UpdatedAt",
                table: "BackupJobs",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupJobs");
        }
    }
}
