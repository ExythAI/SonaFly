using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Entities.Identification;

namespace SonaFlyUI.Server.Infrastructure.Data;

public class SonaFlyDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public SonaFlyDbContext(DbContextOptions<SonaFlyDbContext> options) : base(options) { }

    public DbSet<LibraryRoot> LibraryRoots => Set<LibraryRoot>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<TrackArtist> TrackArtists => Set<TrackArtist>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<TrackGenre> TrackGenres => Set<TrackGenre>();
    public DbSet<ArtworkAsset> ArtworkAssets => Set<ArtworkAsset>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistItem> PlaylistItems => Set<PlaylistItem>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ScanJob> ScanJobs => Set<ScanJob>();
    public DbSet<MixedTape> MixedTapes => Set<MixedTape>();
    public DbSet<MixedTapeItem> MixedTapeItems => Set<MixedTapeItem>();
    public DbSet<UserRestriction> UserRestrictions => Set<UserRestriction>();
    public DbSet<Auditorium> Auditoriums => Set<Auditorium>();
    public DbSet<AuditoriumQueueItem> AuditoriumQueueItems => Set<AuditoriumQueueItem>();

    // ── Music identification (upgrade plan, section 11) ──
    // Additive tables only: the analyzer owns evidence, candidates, decisions,
    // and proposals, while the ordinary scan keeps owning the playable catalog.
    public DbSet<TrackFileRevision> TrackFileRevisions => Set<TrackFileRevision>();
    public DbSet<OriginalTagSnapshot> OriginalTagSnapshots => Set<OriginalTagSnapshot>();
    public DbSet<AcousticFingerprint> AcousticFingerprints => Set<AcousticFingerprint>();
    public DbSet<IdentificationJob> IdentificationJobs => Set<IdentificationJob>();
    public DbSet<IdentificationWorkItem> IdentificationWorkItems => Set<IdentificationWorkItem>();
    public DbSet<ProviderCacheEntry> ProviderCacheEntries => Set<ProviderCacheEntry>();
    public DbSet<RecordingCandidate> RecordingCandidates => Set<RecordingCandidate>();
    public DbSet<AlbumGroup> AlbumGroups => Set<AlbumGroup>();
    public DbSet<AlbumGroupMember> AlbumGroupMembers => Set<AlbumGroupMember>();
    public DbSet<ReleaseCandidate> ReleaseCandidates => Set<ReleaseCandidate>();
    public DbSet<MetadataProposal> MetadataProposals => Set<MetadataProposal>();
    public DbSet<IdentificationError> IdentificationErrors => Set<IdentificationError>();
    public DbSet<ChangeJournal> ChangeJournals => Set<ChangeJournal>();
    public DbSet<CatalogRelease> CatalogReleases => Set<CatalogRelease>();
    public DbSet<CatalogOverride> CatalogOverrides => Set<CatalogOverride>();

    /// <summary>Admin-managed server settings, including encrypted secrets.</summary>
    public DbSet<ServerSetting> ServerSettings => Set<ServerSetting>();

    /// <summary>
    /// The absolute path of the SQLite database file, when this context is backed by one.
    /// Used to keep destructive cache cleanup from deleting the database (backlog N16).
    /// Returns null for in-memory or non-SQLite providers.
    /// </summary>
    public static string? TryGetDatabaseFilePath(SonaFlyDbContext context)
    {
        try
        {
            var connectionString = context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString)) return null;

            var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
            var dataSource = builder.DataSource;

            if (string.IsNullOrWhiteSpace(dataSource) ||
                dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Path.GetFullPath(dataSource);
        }
        catch
        {
            return null;
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ── LibraryRoot ──
        builder.Entity<LibraryRoot>(e =>
        {
            e.HasIndex(x => x.Path).IsUnique();
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Path).HasMaxLength(1024);
        });

        // ── Artist ──
        builder.Entity<Artist>(e =>
        {
            e.HasIndex(x => x.Name);
            e.Property(x => x.Name).HasMaxLength(512);
            e.Property(x => x.SortName).HasMaxLength(512);
            e.HasOne(x => x.Artwork).WithMany().HasForeignKey(x => x.ArtworkId).OnDelete(DeleteBehavior.SetNull);
        });

        // ── Album ──
        builder.Entity<Album>(e =>
        {
            e.HasIndex(x => x.Title);
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.SortTitle).HasMaxLength(512);
            e.HasOne(x => x.AlbumArtist).WithMany(a => a.Albums).HasForeignKey(x => x.AlbumArtistId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Artwork).WithMany().HasForeignKey(x => x.ArtworkId).OnDelete(DeleteBehavior.SetNull);
        });

        // ── Track ──
        builder.Entity<Track>(e =>
        {
            e.HasIndex(x => x.FilePath);
            e.HasIndex(x => x.Title);
            e.HasIndex(x => x.AlbumId);
            e.HasIndex(x => x.PrimaryArtistId);
            e.Property(x => x.FilePath).HasMaxLength(2048);
            e.Property(x => x.FileName).HasMaxLength(512);
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.MimeType).HasMaxLength(128);
            e.HasOne(x => x.LibraryRoot).WithMany(lr => lr.Tracks).HasForeignKey(x => x.LibraryRootId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Album).WithMany(a => a.Tracks).HasForeignKey(x => x.AlbumId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.PrimaryArtist).WithMany(a => a.PrimaryTracks).HasForeignKey(x => x.PrimaryArtistId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.CatalogReleaseId);
            e.HasOne(x => x.CatalogRelease).WithMany().HasForeignKey(x => x.CatalogReleaseId).OnDelete(DeleteBehavior.SetNull);
        });

        // ── TrackArtist (composite key) ──
        builder.Entity<TrackArtist>(e =>
        {
            e.HasKey(x => new { x.TrackId, x.ArtistId });
            e.HasOne(x => x.Track).WithMany(t => t.TrackArtists).HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Artist).WithMany(a => a.TrackArtists).HasForeignKey(x => x.ArtistId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Genre ──
        builder.Entity<Genre>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(256);
        });

        // ── TrackGenre (composite key) ──
        builder.Entity<TrackGenre>(e =>
        {
            e.HasKey(x => new { x.TrackId, x.GenreId });
            e.HasOne(x => x.Track).WithMany(t => t.TrackGenres).HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Genre).WithMany(g => g.TrackGenres).HasForeignKey(x => x.GenreId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── ArtworkAsset ──
        builder.Entity<ArtworkAsset>(e =>
        {
            e.HasIndex(x => x.Hash);
            e.Property(x => x.StoragePath).HasMaxLength(2048);
            e.Property(x => x.MimeType).HasMaxLength(128);
            e.Property(x => x.Hash).HasMaxLength(128);
        });

        // ── Playlist ──
        builder.Entity<Playlist>(e =>
        {
            e.HasIndex(x => x.OwnerUserId);
            e.Property(x => x.Name).HasMaxLength(256);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── PlaylistItem ──
        builder.Entity<PlaylistItem>(e =>
        {
            e.HasOne(x => x.Playlist).WithMany(p => p.Items).HasForeignKey(x => x.PlaylistId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Track).WithMany(t => t.PlaylistItems).HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── RefreshToken ──
        builder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash);
            // Revoking a replayed token's whole chain queries by family.
            e.HasIndex(x => x.FamilyId);
            e.Property(x => x.TokenHash).HasMaxLength(256);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── ScanJob ──
        builder.Entity<ScanJob>(e =>
        {
            e.HasOne(x => x.LibraryRoot).WithMany(lr => lr.ScanJobs).HasForeignKey(x => x.LibraryRootId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── MixedTape ──
        builder.Entity<MixedTape>(e =>
        {
            e.HasIndex(x => x.OwnerUserId);
            e.Property(x => x.Name).HasMaxLength(256);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── MixedTapeItem ──
        builder.Entity<MixedTapeItem>(e =>
        {
            e.HasOne(x => x.MixedTape).WithMany(m => m.Items).HasForeignKey(x => x.MixedTapeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── UserRestriction ──
        builder.Entity<UserRestriction>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.RestrictionType, x.TargetId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Property(x => x.TargetName).HasMaxLength(512);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Auditorium ──
        builder.Entity<Auditorium>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── AuditoriumQueueItem ──
        builder.Entity<AuditoriumQueueItem>(e =>
        {
            e.HasOne(x => x.Auditorium).WithMany(a => a.QueueItems).HasForeignKey(x => x.AuditoriumId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.QueuedByUser).WithMany().HasForeignKey(x => x.QueuedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.AuditoriumId, x.Position });
        });

        ConfigureIdentification(builder);
    }

    /// <summary>
    /// Additive music-identification tables (upgrade plan, section 11).
    /// Identification enums are persisted as strings so values can be appended
    /// safely. No unique constraint on SHA-256: identical bytes are legitimate.
    /// Purging a library cascades through LibraryRoot-owned rows, while the
    /// audit journal keeps plain IDs so retention policy decides its lifetime.
    /// </summary>
    private static void ConfigureIdentification(ModelBuilder builder)
    {
        builder.Entity<TrackFileRevision>(e =>
        {
            e.HasIndex(x => new { x.LibraryRootId, x.NormalizedPath });
            e.HasIndex(x => x.TrackId);
            e.HasIndex(x => x.Sha256);
            e.Property(x => x.NormalizedPath).HasMaxLength(2048);
            e.Property(x => x.RelativePath).HasMaxLength(2048);
            e.Property(x => x.Sha256).HasMaxLength(128);
            e.Property(x => x.HashAlgorithm).HasMaxLength(32);
            e.Property(x => x.Codec).HasMaxLength(64);
            e.Property(x => x.ParseStatus).HasMaxLength(64);
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OriginalTagSnapshot>(e =>
        {
            e.HasIndex(x => x.FileRevisionId).IsUnique();
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.Album).HasMaxLength(512);
            e.Property(x => x.Artist).HasMaxLength(512);
            e.Property(x => x.AlbumArtist).HasMaxLength(512);
            e.Property(x => x.Genre).HasMaxLength(256);
            e.Property(x => x.FullDate).HasMaxLength(64);
            e.Property(x => x.Isrc).HasMaxLength(32);
            e.Property(x => x.Barcode).HasMaxLength(64);
            e.Property(x => x.CatalogNumber).HasMaxLength(128);
            e.Property(x => x.MusicBrainzRecordingId).HasMaxLength(64);
            e.Property(x => x.MusicBrainzReleaseId).HasMaxLength(64);
            e.Property(x => x.MusicBrainzReleaseGroupId).HasMaxLength(64);
            e.Property(x => x.RawFieldsJson).HasMaxLength(8000);
            e.Property(x => x.ParserVersion).HasMaxLength(128);
            e.HasOne(x => x.FileRevision).WithMany().HasForeignKey(x => x.FileRevisionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AcousticFingerprint>(e =>
        {
            e.HasIndex(x => x.FileRevisionId);
            e.HasIndex(x => x.FingerprintDigest);
            e.Property(x => x.Algorithm).HasMaxLength(64);
            e.Property(x => x.ToolVersion).HasMaxLength(128);
            e.Property(x => x.FingerprintDigest).HasMaxLength(128);
            e.Property(x => x.Error).HasMaxLength(1024);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.FileRevision).WithMany().HasForeignKey(x => x.FileRevisionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<IdentificationJob>(e =>
        {
            e.HasIndex(x => new { x.LibraryRootId, x.Status });
            e.HasIndex(x => x.ClientRequestKey).IsUnique();
            e.Property(x => x.ClientRequestKey).HasMaxLength(128);
            e.Property(x => x.SelectionMode).HasMaxLength(32);
            e.Property(x => x.SelectionJson).HasMaxLength(8000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Stage).HasMaxLength(64);
            e.Property(x => x.LeaseOwner).HasMaxLength(128);
            e.Property(x => x.LastCheckpoint).HasMaxLength(512);
            e.Property(x => x.ErrorSummary).HasMaxLength(2048);
            e.HasOne(x => x.LibraryRoot).WithMany().HasForeignKey(x => x.LibraryRootId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<IdentificationWorkItem>(e =>
        {
            e.HasIndex(x => new { x.JobId, x.Status });
            e.HasIndex(x => x.TrackId);
            e.HasIndex(x => x.NextRetryUtc);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Stage).HasMaxLength(64);
            e.Property(x => x.LeaseOwner).HasMaxLength(128);
            e.Property(x => x.LastError).HasMaxLength(1024);
            e.Property(x => x.ErrorCategory).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Job).WithMany(j => j.WorkItems).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProviderCacheEntry>(e =>
        {
            e.HasIndex(x => new { x.Provider, x.CacheKeyDigest, x.QueryShape }).IsUnique();
            e.Property(x => x.Provider).HasMaxLength(64);
            e.Property(x => x.CacheKeyDigest).HasMaxLength(128);
            e.Property(x => x.QueryShape).HasMaxLength(512);
            e.Property(x => x.ResponseJson).HasMaxLength(200000);
            e.Property(x => x.SchemaVersion).HasMaxLength(32);
        });

        builder.Entity<RecordingCandidate>(e =>
        {
            e.HasIndex(x => x.WorkItemId);
            e.HasIndex(x => x.MusicBrainzRecordingId);
            e.Property(x => x.Provider).HasMaxLength(64);
            e.Property(x => x.AcoustId).HasMaxLength(64);
            e.Property(x => x.MusicBrainzRecordingId).HasMaxLength(64);
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.Artist).HasMaxLength(512);
            e.Property(x => x.ConfidenceBand).HasMaxLength(32);
            e.Property(x => x.ScoringVersion).HasMaxLength(64);
            e.Property(x => x.ScoringBreakdownJson).HasMaxLength(8000);
            e.Property(x => x.Conflicts).HasMaxLength(2048);
            e.Property(x => x.Provenance).HasMaxLength(1024);
            e.HasOne(x => x.WorkItem).WithMany().HasForeignKey(x => x.WorkItemId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AlbumGroup>(e =>
        {
            e.HasIndex(x => new { x.JobId, x.GroupKey });
            e.Property(x => x.GroupKey).HasMaxLength(512);
            e.Property(x => x.EvidenceJson).HasMaxLength(8000);
            e.Property(x => x.Status).HasMaxLength(32);
            e.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AlbumGroupMember>(e =>
        {
            e.HasIndex(x => x.GroupId);
            e.HasIndex(x => x.TrackId);
            e.HasOne(x => x.Group).WithMany(g => g.Members).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ReleaseCandidate>(e =>
        {
            e.HasIndex(x => x.GroupId);
            e.HasIndex(x => x.MusicBrainzReleaseId);
            e.Property(x => x.MusicBrainzReleaseId).HasMaxLength(64);
            e.Property(x => x.MusicBrainzReleaseGroupId).HasMaxLength(64);
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.Artist).HasMaxLength(512);
            e.Property(x => x.Date).HasMaxLength(32);
            e.Property(x => x.Country).HasMaxLength(16);
            e.Property(x => x.Label).HasMaxLength(256);
            e.Property(x => x.CatalogNumber).HasMaxLength(128);
            e.Property(x => x.Barcode).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(64);
            e.Property(x => x.Format).HasMaxLength(64);
            e.Property(x => x.ScoringBreakdownJson).HasMaxLength(8000);
            e.Property(x => x.AmbiguitySetId).HasMaxLength(128);
            e.Property(x => x.ScoringVersion).HasMaxLength(64);
            e.Property(x => x.Provenance).HasMaxLength(1024);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MetadataProposal>(e =>
        {
            e.HasIndex(x => new { x.JobId, x.Status });
            e.HasIndex(x => new { x.TrackId, x.Status });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.FieldMask).HasMaxLength(512);
            e.Property(x => x.OldTitle).HasMaxLength(512);
            e.Property(x => x.NewTitle).HasMaxLength(512);
            e.Property(x => x.OldArtist).HasMaxLength(512);
            e.Property(x => x.NewArtist).HasMaxLength(512);
            e.Property(x => x.OldAlbum).HasMaxLength(512);
            e.Property(x => x.NewAlbum).HasMaxLength(512);
            e.Property(x => x.EvidenceSnapshotJson).HasMaxLength(8000);
            e.Property(x => x.ConcurrencyToken).HasMaxLength(64);
            e.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<IdentificationError>(e =>
        {
            e.HasIndex(x => x.JobId);
            e.HasIndex(x => x.WorkItemId);
            e.Property(x => x.Subsystem).HasMaxLength(128);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Message).HasMaxLength(1024);
        });

        builder.Entity<ChangeJournal>(e =>
        {
            e.HasIndex(x => x.TrackId);
            e.HasIndex(x => x.ProposalId);
            e.Property(x => x.OldPath).HasMaxLength(2048);
            e.Property(x => x.NewPath).HasMaxLength(2048);
            e.Property(x => x.OldTagSnapshotJson).HasMaxLength(8000);
            e.Property(x => x.NewTagSnapshotJson).HasMaxLength(8000);
            e.Property(x => x.OldSha256).HasMaxLength(128);
            e.Property(x => x.NewSha256).HasMaxLength(128);
            e.Property(x => x.StepsAttemptedJson).HasMaxLength(4000);
            e.Property(x => x.StepsCompletedJson).HasMaxLength(4000);
            e.Property(x => x.Error).HasMaxLength(2048);
        });

        // ── Server settings (admin-managed secrets) ──
        builder.Entity<ServerSetting>(e =>
        {
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(128);
            e.Property(x => x.EncryptedValue).HasMaxLength(2048);
        });

        // ── Approved exact releases (backlog U01) ──
        // Album rows stay title-plus-artist; editions live here so two
        // editions never collapse and ambiguous music simply links nothing.
        builder.Entity<CatalogRelease>(e =>
        {
            e.HasIndex(x => x.AlbumId);
            e.HasIndex(x => x.MusicBrainzReleaseId);
            e.Property(x => x.Title).HasMaxLength(512);
            e.Property(x => x.ArtistName).HasMaxLength(512);
            e.Property(x => x.MusicBrainzReleaseId).HasMaxLength(64);
            e.Property(x => x.MusicBrainzReleaseGroupId).HasMaxLength(64);
            e.Property(x => x.Date).HasMaxLength(32);
            e.Property(x => x.Country).HasMaxLength(16);
            e.Property(x => x.Label).HasMaxLength(256);
            e.Property(x => x.CatalogNumber).HasMaxLength(128);
            e.Property(x => x.Barcode).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(64);
            e.Property(x => x.Format).HasMaxLength(64);
            e.HasOne(x => x.Album).WithMany().HasForeignKey(x => x.AlbumId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Approved catalog overrides (backlog U04) ──
        // One active row per track and field. Clearing deletes the row;
        // history stays in the proposal and change journal.
        builder.Entity<CatalogOverride>(e =>
        {
            e.HasIndex(x => new { x.TrackId, x.Field }).IsUnique();
            e.Property(x => x.Field).HasMaxLength(64);
            e.Property(x => x.ValueText).HasMaxLength(512);
            e.Property(x => x.SourceFingerprintDigest).HasMaxLength(128);
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
