using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SonaFlyUI.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddScanJobFilesUnverified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FilesUnverified",
                table: "ScanJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilesUnverified",
                table: "ScanJobs");
        }
    }
}
