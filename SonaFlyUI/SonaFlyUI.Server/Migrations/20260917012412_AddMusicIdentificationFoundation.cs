using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SonaFlyUI.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMusicIdentificationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChangeJournals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OldPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    NewPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    OldTagSnapshotJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    NewTagSnapshotJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    OldSha256 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    NewSha256 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    StepsAttemptedJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    StepsCompletedJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ReviewerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeJournals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdentificationErrors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    WorkItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Subsystem = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Retryable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NextRetryUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentificationErrors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdentificationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryRootId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientRequestKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SelectionMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SelectionJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TotalItems = table.Column<int>(type: "INTEGER", nullable: false),
                    HashedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FingerprintedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LookedUpCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ResolvedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AmbiguousCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ProposalsReadyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextRetryUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LeaseExpiryUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastCheckpoint = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ErrorSummary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentificationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdentificationJobs_LibraryRoots_LibraryRootId",
                        column: x => x.LibraryRootId,
                        principalTable: "LibraryRoots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProviderCacheEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CacheKeyDigest = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    QueryShape = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    HttpStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    ResponseJson = table.Column<string>(type: "TEXT", maxLength: 200000, nullable: true),
                    FetchedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsNotFound = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCacheEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackFileRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryRootId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NormalizedPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedUtcSource = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Sha256CalculatedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HashAlgorithm = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Codec = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Channels = table.Column<int>(type: "INTEGER", nullable: true),
                    BitDepth = table.Column<int>(type: "INTEGER", nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ParseStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackFileRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackFileRevisions_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AlbumGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryRootId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    EvidenceJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbumGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlbumGroups_IdentificationJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "IdentificationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IdentificationWorkItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileRevisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextRetryUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LeaseExpiryUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ErrorCategory = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Retryable = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentificationWorkItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdentificationWorkItems_IdentificationJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "IdentificationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MetadataProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecordingCandidateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReleaseCandidateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpectedFileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedModifiedUtcSource = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FieldMask = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    OldTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    NewTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    OldArtist = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    NewArtist = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    OldAlbum = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    NewAlbum = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    OldTrackNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    NewTrackNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    OldDiscNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    NewDiscNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    OldYear = table.Column<int>(type: "INTEGER", nullable: true),
                    NewYear = table.Column<int>(type: "INTEGER", nullable: true),
                    EvidenceSnapshotJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReviewedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AppliedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetadataProposals_IdentificationJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "IdentificationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AcousticFingerprints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Algorithm = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ToolVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    FingerprintDigest = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcousticFingerprints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcousticFingerprints_TrackFileRevisions_FileRevisionId",
                        column: x => x.FileRevisionId,
                        principalTable: "TrackFileRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OriginalTagSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Album = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Artist = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    AlbumArtist = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Genre = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    TrackNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    TrackTotal = table.Column<int>(type: "INTEGER", nullable: true),
                    DiscNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    DiscTotal = table.Column<int>(type: "INTEGER", nullable: true),
                    FullDate = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Isrc = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Barcode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CatalogNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MusicBrainzRecordingId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MusicBrainzReleaseId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MusicBrainzReleaseGroupId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RawFieldsJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    ParserVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CapturedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OriginalTagSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OriginalTagSnapshots_TrackFileRevisions_FileRevisionId",
                        column: x => x.FileRevisionId,
                        principalTable: "TrackFileRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AlbumGroupMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileRevisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProposedDisc = table.Column<int>(type: "INTEGER", nullable: true),
                    ProposedPosition = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbumGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlbumGroupMembers_AlbumGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "AlbumGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MusicBrainzReleaseId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MusicBrainzReleaseGroupId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Artist = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Date = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CatalogNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Barcode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Format = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Score = table.Column<double>(type: "REAL", nullable: true),
                    ScoringBreakdownJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    IsAmbiguityMember = table.Column<bool>(type: "INTEGER", nullable: false),
                    AmbiguitySetId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ScoringVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Provenance = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseCandidates_AlbumGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "AlbumGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecordingCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileRevisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AcoustId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MusicBrainzRecordingId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Artist = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    RawProviderScore = table.Column<double>(type: "REAL", nullable: true),
                    FinalScore = table.Column<double>(type: "REAL", nullable: true),
                    ConfidenceBand = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ScoringVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ScoringBreakdownJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    Conflicts = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Provenance = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    FetchedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordingCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecordingCandidates_IdentificationWorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "IdentificationWorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcousticFingerprints_FileRevisionId",
                table: "AcousticFingerprints",
                column: "FileRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_AcousticFingerprints_FingerprintDigest",
                table: "AcousticFingerprints",
                column: "FingerprintDigest");

            migrationBuilder.CreateIndex(
                name: "IX_AlbumGroupMembers_GroupId",
                table: "AlbumGroupMembers",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AlbumGroupMembers_TrackId",
                table: "AlbumGroupMembers",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_AlbumGroups_JobId_GroupKey",
                table: "AlbumGroups",
                columns: new[] { "JobId", "GroupKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeJournals_ProposalId",
                table: "ChangeJournals",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeJournals_TrackId",
                table: "ChangeJournals",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationErrors_JobId",
                table: "IdentificationErrors",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationErrors_WorkItemId",
                table: "IdentificationErrors",
                column: "WorkItemId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationJobs_ClientRequestKey",
                table: "IdentificationJobs",
                column: "ClientRequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationJobs_LibraryRootId_Status",
                table: "IdentificationJobs",
                columns: new[] { "LibraryRootId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationWorkItems_JobId_Status",
                table: "IdentificationWorkItems",
                columns: new[] { "JobId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationWorkItems_NextRetryUtc",
                table: "IdentificationWorkItems",
                column: "NextRetryUtc");

            migrationBuilder.CreateIndex(
                name: "IX_IdentificationWorkItems_TrackId",
                table: "IdentificationWorkItems",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_MetadataProposals_JobId_Status",
                table: "MetadataProposals",
                columns: new[] { "JobId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MetadataProposals_TrackId_Status",
                table: "MetadataProposals",
                columns: new[] { "TrackId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OriginalTagSnapshots_FileRevisionId",
                table: "OriginalTagSnapshots",
                column: "FileRevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCacheEntries_Provider_CacheKeyDigest_QueryShape",
                table: "ProviderCacheEntries",
                columns: new[] { "Provider", "CacheKeyDigest", "QueryShape" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecordingCandidates_MusicBrainzRecordingId",
                table: "RecordingCandidates",
                column: "MusicBrainzRecordingId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordingCandidates_WorkItemId",
                table: "RecordingCandidates",
                column: "WorkItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCandidates_GroupId",
                table: "ReleaseCandidates",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCandidates_MusicBrainzReleaseId",
                table: "ReleaseCandidates",
                column: "MusicBrainzReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackFileRevisions_LibraryRootId_NormalizedPath",
                table: "TrackFileRevisions",
                columns: new[] { "LibraryRootId", "NormalizedPath" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackFileRevisions_Sha256",
                table: "TrackFileRevisions",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_TrackFileRevisions_TrackId",
                table: "TrackFileRevisions",
                column: "TrackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcousticFingerprints");

            migrationBuilder.DropTable(
                name: "AlbumGroupMembers");

            migrationBuilder.DropTable(
                name: "ChangeJournals");

            migrationBuilder.DropTable(
                name: "IdentificationErrors");

            migrationBuilder.DropTable(
                name: "MetadataProposals");

            migrationBuilder.DropTable(
                name: "OriginalTagSnapshots");

            migrationBuilder.DropTable(
                name: "ProviderCacheEntries");

            migrationBuilder.DropTable(
                name: "RecordingCandidates");

            migrationBuilder.DropTable(
                name: "ReleaseCandidates");

            migrationBuilder.DropTable(
                name: "TrackFileRevisions");

            migrationBuilder.DropTable(
                name: "IdentificationWorkItems");

            migrationBuilder.DropTable(
                name: "AlbumGroups");

            migrationBuilder.DropTable(
                name: "IdentificationJobs");
        }
    }
}
