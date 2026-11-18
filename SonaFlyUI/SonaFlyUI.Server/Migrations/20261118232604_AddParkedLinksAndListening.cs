using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SonaFlyUI.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddParkedLinksAndListening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FavoriteTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FavoriteTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FavoriteTracks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FavoriteTracks_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParkedLibraryLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RestrictionType = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    TargetArtistName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkedLibraryLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayEvents_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayEvents_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteTracks_TrackId",
                table: "FavoriteTracks",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteTracks_UserId_TrackId",
                table: "FavoriteTracks",
                columns: new[] { "UserId", "TrackId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParkedLibraryLinks_FilePath",
                table: "ParkedLibraryLinks",
                column: "FilePath");

            migrationBuilder.CreateIndex(
                name: "IX_ParkedLibraryLinks_Kind",
                table: "ParkedLibraryLinks",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_TrackId",
                table: "PlayEvents",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_UserId_PlayedUtc",
                table: "PlayEvents",
                columns: new[] { "UserId", "PlayedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FavoriteTracks");

            migrationBuilder.DropTable(
                name: "ParkedLibraryLinks");

            migrationBuilder.DropTable(
                name: "PlayEvents");
        }
    }
}
