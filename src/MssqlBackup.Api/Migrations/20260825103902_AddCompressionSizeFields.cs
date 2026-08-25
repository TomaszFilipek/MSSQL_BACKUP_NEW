using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MssqlBackup.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompressionSizeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FileSizeAfterCompression",
                table: "BackupRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBeforeCompression",
                table: "BackupRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileSizeAfterCompression",
                table: "BackupRecords");

            migrationBuilder.DropColumn(
                name: "FileSizeBeforeCompression",
                table: "BackupRecords");
        }
    }
}
