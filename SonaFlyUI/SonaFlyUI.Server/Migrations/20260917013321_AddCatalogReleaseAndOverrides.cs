using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SonaFlyUI.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogReleaseAndOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CatalogReleaseId",
                table: "Tracks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CatalogOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Field = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ValueText = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ValueId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProposalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceFingerprintDigest = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SourceFileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogOverrides_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryRootId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AlbumId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ArtistName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    MusicBrainzReleaseId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MusicBrainzReleaseGroupId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Date = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CatalogNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Barcode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Format = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceProposalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogReleases_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_CatalogReleaseId",
                table: "Tracks",
                column: "CatalogReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogOverrides_TrackId_Field",
                table: "CatalogOverrides",
                columns: new[] { "TrackId", "Field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogReleases_AlbumId",
                table: "CatalogReleases",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogReleases_MusicBrainzReleaseId",
                table: "CatalogReleases",
                column: "MusicBrainzReleaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tracks_CatalogReleases_CatalogReleaseId",
                table: "Tracks",
                column: "CatalogReleaseId",
                principalTable: "CatalogReleases",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tracks_CatalogReleases_CatalogReleaseId",
                table: "Tracks");

            migrationBuilder.DropTable(
                name: "CatalogOverrides");

            migrationBuilder.DropTable(
                name: "CatalogReleases");

            migrationBuilder.DropIndex(
                name: "IX_Tracks_CatalogReleaseId",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "CatalogReleaseId",
                table: "Tracks");
        }
    }
}
