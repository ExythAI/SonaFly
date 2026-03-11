using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Domain.Entities;

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
    }
}
