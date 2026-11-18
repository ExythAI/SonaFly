using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SonaFlyUI.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchGenre",
                table: "Tracks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchTitle",
                table: "Tracks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchName",
                table: "Artists",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchTitle",
                table: "Albums",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchGenre",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "SearchTitle",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "SearchName",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "SearchTitle",
                table: "Albums");
        }
    }
}
