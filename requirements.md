# SonaFly MVP Specification

## 1. Purpose

SonaFly is a personal music server with:

* An **ASP.NET backend** that manages users, library indexing, metadata, artwork, playlists, and authenticated audio streaming
* A **React admin UI** with a **dark theme** for server administration
* A future **Android client** that consumes the same API

This document is intended as an implementation-ready handoff for a coding agent. It defines the MVP scope, architecture, data model, API surface, storage model, coding standards, and delivery plan.

---

## 2. Core Requirements

### Functional

* Configure one or more music library folders
* Support Docker deployment
* Support host-mounted folders and shared network locations
* Scan audio files and build a local music library database
* Read embedded metadata from files
* Extract or cache artwork on the server
* Provide search and browse by artist, album, track, genre, playlist
* Provide authenticated audio streaming to clients
* Manage users (create, disable, delete, reset password, role assignment)
* Manage playlists
* Expose a REST API for both admin UI and future Android client
* Provide scan status and re-scan operations

### Non-Functional

* Use dependency injection properly
* Use clean separation of concerns
* Use robust coding patterns suitable for growth
* Keep MVP simple enough to finish
* Persist data outside the container
* Avoid brittle assumptions about file paths
* Do not load full audio files into memory when streaming

---

## 3. Critical Runtime Constraint

**Do not design the container to directly depend on a raw Windows UNC path as its internal runtime path.**

Testing path provided by user:

* `\\192.168.0.106\music`

That path is acceptable as a **host-side source**, but the container should consume a **mounted path**, for example:

* Host mounts `\\192.168.0.106\music`
* Container receives it as `/music/library-main`

The application should only work with container-visible paths such as:

* `/music/library-main`
* `/music/library-secondary`

The admin UI may store a friendly display name and source description, but the backend runtime must only scan mounted paths that actually exist inside the container.

---

## 4. MVP Scope

### In Scope

* Single-server deployment
* Local authentication with roles
* Music library scanning
* Metadata ingestion from file tags
* Artwork extraction and server-side caching
* Library browsing and search
* Playlist CRUD
* Audio streaming with HTTP range support
* Dark admin UI
* SQLite-backed persistence for MVP
* Background scan service inside the ASP.NET host

### Out of Scope for MVP

* Internet-scale multi-tenant hosting
* Distributed processing
* Smart recommendations
* Lyrics sync
* Real-time collaborative playlists
* Full transcoding matrix
* DLNA / AirPlay / Chromecast
* Social sharing
* Offline sync logic for Android
* Multi-node clustering

### Explicit MVP Decision

For MVP, **stream original files** rather than building a full transcoding pipeline. Add transcoding later only if required for incompatible formats or bandwidth reduction.

---

## 5. Recommended Technology Stack

### Backend

* **ASP.NET Core Web API**
* **Entity Framework Core**
* **ASP.NET Core Identity** for users, roles, password hashing, auth flows
* **SQLite** for MVP metadata and user database
* **JWT access tokens** + refresh tokens
* **TagLib#** (or equivalent .NET audio tag reader) for metadata extraction
* Built-in **BackgroundService** / hosted services for scanning jobs
* Structured logging

### Frontend

* **React + TypeScript**
* **React Router**
* **TanStack Query** for API state
* **Material UI** or equivalent component library with a strict dark theme
* Axios or fetch wrapper with auth token handling

### Storage

* Audio files stored in mounted volumes
* Database stored on persistent volume
* Artwork cache stored on persistent volume
* Logs stored on persistent volume

### Docker

* One container for API + server-hosted React static assets in MVP
* Persistent volumes for data, cache, logs
* Mounted host music folders for library roots

---

## 6. Architecture Principles

### Required Principles

* Keep **Domain**, **Application**, **Infrastructure**, and **API** concerns separated
* Prefer **composition over inheritance**, but use inheritance where it removes real duplication
* Use **constructor injection** everywhere
* Keep file system logic out of controllers
* Keep EF Core logic out of UI code and streaming controllers where possible
* Keep scanning, metadata extraction, artwork handling, and streaming behind interfaces

### Important Design Rule

Do **not** build a useless generic repository abstraction on top of EF Core. That is ceremony without value.

Use:

* EF Core `DbContext` in Infrastructure
* Specific repositories only where needed
* Application services / query services for business workflows

### Suggested Layers

* **SonaFly.Domain**

  * Entities
  * Value objects
  * Enums
  * Domain rules

* **SonaFly.Application**

  * DTOs
  * Service interfaces
  * Commands / queries or application services
  * Validation

* **SonaFly.Infrastructure**

  * EF Core
  * Identity
  * File scanning
  * Metadata extraction
  * Artwork storage
  * Token services
  * Hosted background services

* **SonaFly.Api**

  * Controllers / minimal endpoints
  * Authentication setup
  * DI registration
  * Swagger in development
  * Static file hosting for admin UI

* **SonaFly.Admin**

  * React admin UI

---

## 7. Proposed Solution Structure

```text
SonaFly/
  src/
    SonaFly.Domain/
    SonaFly.Application/
    SonaFly.Infrastructure/
    SonaFly.Api/
    SonaFly.Admin/
  docker/
    docker-compose.yml
    Dockerfile
  docs/
    SonaFly_MVP_Backend_Admin_Spec.md
```

### Alternative if keeping current Visual Studio template

If the blank Visual Studio project already contains ASP.NET + React in one solution, keep that shell, but still split backend code internally into:

* Domain
* Application
* Infrastructure
* Api

Do not keep all logic in controllers or Program.cs. That becomes garbage fast.

---

## 8. Storage Model

### Persistent Paths Inside Container

Use environment variables so the app never hardcodes storage locations.

```env
SONAFLY_DATA_ROOT=/app/data
SONAFLY_DB_PATH=/app/data/db/sonafly.db
SONAFLY_ARTWORK_ROOT=/app/data/artwork
SONAFLY_LOG_ROOT=/app/data/logs
SONAFLY_LIBRARY_ROOTS=/music/library-main;/music/library-secondary
```

### Docker Volume Strategy

* `/app/data/db` → persistent database volume
* `/app/data/artwork` → persistent artwork cache volume
* `/app/data/logs` → persistent logs volume
* `/music/library-main` → mounted host path or mounted network share

### Example Compose Concept

```yaml
services:
  sonafly:
    build:
      context: .
      dockerfile: docker/Dockerfile
    ports:
      - "8080:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      SONAFLY_DATA_ROOT: /app/data
      SONAFLY_DB_PATH: /app/data/db/sonafly.db
      SONAFLY_ARTWORK_ROOT: /app/data/artwork
      SONAFLY_LOG_ROOT: /app/data/logs
    volumes:
      - sonafly_db:/app/data/db
      - sonafly_artwork:/app/data/artwork
      - sonafly_logs:/app/data/logs
      - /mnt/music:/music/library-main:ro

volumes:
  sonafly_db:
  sonafly_artwork:
  sonafly_logs:
```

### Windows / Network Share Testing

For testing against `\\192.168.0.106\music`, mount that share on the host first, then pass the mounted location into Docker.

Examples:

* Windows host maps the share to a drive or mounted folder
* Docker mounts that host-visible location into the container
* The app scans `/music/library-main`

---

## 9. Domain Model

### Base Types

Use minimal inheritance only where it adds value.

```text
EntityBase
  - Id
  - CreatedUtc
  - ModifiedUtc

AuditableEntity : EntityBase
  - CreatedBy
  - ModifiedBy
```

### Identity

Use ASP.NET Identity with GUID keys.

```text
ApplicationUser : IdentityUser<Guid>
  - DisplayName
  - IsEnabled
  - LastLoginUtc
  - CreatedUtc

ApplicationRole : IdentityRole<Guid>
```

### Core Music Entities

#### LibraryRoot

Represents a configured library mount.

Fields:

* Id
* Name
* Path
* IsEnabled
* IsReadOnly
* LastScanStartedUtc
* LastScanCompletedUtc
* LastScanStatus
* LastScanError

#### Artist

Fields:

* Id
* Name
* SortName
* MusicBrainzId (nullable, future-ready)
* ArtworkId (nullable)

#### Album

Fields:

* Id
* Title
* SortTitle
* AlbumArtistId (nullable)
* Year
* DiscCount
* TrackCount
* GenreSummary
* ArtworkId (nullable)

#### Track

Fields:

* Id
* LibraryRootId
* FilePath
* FileName
* FileExtension
* FileSizeBytes
* DurationSeconds
* BitRateKbps
* SampleRateHz
* TrackNumber
* DiscNumber
* Title
* SortTitle
* AlbumId
* PrimaryArtistId (nullable)
* Genre
* MimeType
* ContentHash (optional for MVP)
* ModifiedUtcSource
* IsIndexed
* IsMissing

#### TrackArtist

Many-to-many relation if needed beyond a single primary artist.

Fields:

* TrackId
* ArtistId
* Role

#### Genre

Fields:

* Id
* Name

#### TrackGenre

Fields:

* TrackId
* GenreId

#### ArtworkAsset

Represents cached server artwork.

Fields:

* Id
* StoragePath
* MimeType
* Width
* Height
* FileSizeBytes
* Hash
* SourceType
* SourceTrackId (nullable)
* SourceAlbumId (nullable)

#### Playlist

Fields:

* Id
* Name
* Description
* OwnerUserId
* IsSystemPlaylist
* IsPublic

#### PlaylistItem

Fields:

* Id
* PlaylistId
* TrackId
* SortOrder
* AddedUtc

#### RefreshToken

Fields:

* Id
* UserId
* TokenHash
* ExpiresUtc
* RevokedUtc
* CreatedUtc
* ReplacedByTokenHash

#### ScanJob

Fields:

* Id
* LibraryRootId
* Status
* StartedUtc
* CompletedUtc
* FilesScanned
* FilesAdded
* FilesUpdated
* FilesMissing
* ErrorsCount
* ErrorSummary

---

## 10. Metadata Strategy

### Source of Truth Order

1. **Embedded file tags**
2. Existing cached metadata in DB
3. Optional manual overrides later
4. External metadata providers later

### Supported Audio Types for MVP

* `.mp3`
* `.flac`
* `.m4a`
* `.aac`
* `.ogg`
* `.opus`
* `.wav`

### Metadata to Read

* Title
* Album
* Artist
* Album artist
* Track number
* Disc number
* Genre
* Year
* Duration
* Bitrate
* Sample rate
* Embedded artwork

### Extraction Rules

* Prefer embedded tags over folder-name guessing
* If tags are missing, use fallback parsing from filename only as a weak fallback
* Normalize whitespace and trim values
* Do not create duplicate artists/albums because of casing or trailing spaces
* Store original file path and source timestamps to support incremental scans

### Artwork Rules

* If embedded artwork exists, extract it once and store it in server artwork storage
* Deduplicate artwork by hash where practical
* Tracks may point to album artwork if album-level art exists
* Serve artwork through API, not by exposing raw file system paths

---

## 11. Scanning and Library Refresh

### Scanning Requirements

* Full scan of configured library roots
* Incremental re-scan based on file modified time and size
* Mark missing files when paths disappear
* Do not delete track history immediately when files vanish; mark missing first
* Provide scan progress status to admin UI

### Background Services

Implement:

* `LibraryScanBackgroundService`
* `IFileScanner`
* `IMetadataReader`
* `IArtworkService`
* `ILibraryIndexService`
* `IScanQueue`

### Scan Workflow

1. Load enabled `LibraryRoot` records
2. Enumerate files recursively
3. Filter supported extensions
4. Compare file path + size + last modified against DB
5. Read metadata for changed/new files
6. Upsert artists, albums, tracks, genres
7. Extract/cache artwork
8. Mark orphaned DB records as missing if file no longer exists
9. Save job stats

### Trigger Types

* Manual scan from admin UI
* Scan on startup (configurable)
* Scheduled periodic scan (configurable)

### Do Not Do for MVP

* OS-specific file system watchers across network shares as the primary sync strategy

Reason:

* Network shares and containerized environments make watcher behavior unreliable and harder to debug

Use scheduled scans instead.

---

## 12. Audio Streaming Requirements

### Mandatory Streaming Behavior

* Authenticated access
* HTTP range request support
* Correct `Content-Type`
* Efficient file streaming using file streams
* No full-file memory buffering
* Support seeking

### Endpoint Concept

* `GET /api/stream/tracks/{trackId}`

### Streaming Notes

* Streaming original files is fine for MVP
* The future Android client should handle queueing and pre-buffering between songs
* “Gapless” feeling is mostly a client playback concern; the server’s job is to provide fast range-capable access

### Optional Later

* FFmpeg-based transcoding
* HLS
* On-the-fly format conversion

Do not burden the MVP with that yet.

---

## 13. Authentication and Authorization

### Auth Model

* Username/email + password login
* JWT access token
* Refresh token rotation
* Role-based authorization

### Roles

* `Admin`
* `User`

### Admin Permissions

* Manage users
* Manage library roots
* Trigger scans
* View logs/status
* Manage playlists globally if required

### User Permissions

* Read library
* Stream tracks
* Manage own playlists
* Read artwork

### Security Requirements

* Passwords must be hashed using ASP.NET Identity defaults
* Never store plain refresh tokens; store hashed tokens
* Do not expose real file system paths to clients
* Sanitize all path handling
* Restrict streaming to indexed files only
* Ensure disabled users cannot authenticate

---

## 14. API Surface

Use REST endpoints with pagination where needed.

### Auth

* `POST /api/auth/login`
* `POST /api/auth/refresh`
* `POST /api/auth/logout`
* `GET /api/auth/me`

### Users

* `GET /api/users`
* `GET /api/users/{id}`
* `POST /api/users`
* `PUT /api/users/{id}`
* `POST /api/users/{id}/disable`
* `POST /api/users/{id}/enable`
* `POST /api/users/{id}/reset-password`
* `DELETE /api/users/{id}`

### Library Roots

* `GET /api/library-roots`
* `POST /api/library-roots`
* `PUT /api/library-roots/{id}`
* `DELETE /api/library-roots/{id}`
* `POST /api/library-roots/{id}/scan`

### Scan / Jobs

* `GET /api/scans`
* `GET /api/scans/{id}`
* `GET /api/scans/current`

### Browse

* `GET /api/artists`
* `GET /api/artists/{id}`
* `GET /api/albums`
* `GET /api/albums/{id}`
* `GET /api/tracks`
* `GET /api/tracks/{id}`
* `GET /api/genres`
* `GET /api/search?q=`

### Playlists

* `GET /api/playlists`
* `GET /api/playlists/{id}`
* `POST /api/playlists`
* `PUT /api/playlists/{id}`
* `DELETE /api/playlists/{id}`
* `POST /api/playlists/{id}/items`
* `PUT /api/playlists/{id}/items/reorder`
* `DELETE /api/playlists/{id}/items/{itemId}`

### Artwork

* `GET /api/artwork/{id}`
* `GET /api/albums/{id}/artwork`
* `GET /api/artists/{id}/artwork`
* `GET /api/tracks/{id}/artwork`

### Streaming

* `GET /api/stream/tracks/{id}`

### Health / Diagnostics

* `GET /api/health`
* `GET /api/system/status`

---

## 15. API Response Conventions

### Standard Response Patterns

* Return DTOs, never EF entities directly
* Use pagination for list-heavy endpoints
* Use problem details for validation and errors
* Use consistent sort/filter models

### Recommended DTO Examples

#### TrackListItemDto

* Id
* Title
* ArtistName
* AlbumTitle
* TrackNumber
* DurationSeconds
* ArtworkUrl
* StreamUrl

#### AlbumDetailDto

* Id
* Title
* ArtistName
* Year
* ArtworkUrl
* Tracks[]

#### ScanJobDto

* Id
* Status
* StartedUtc
* CompletedUtc
* FilesScanned
* FilesAdded
* FilesUpdated
* FilesMissing
* ErrorsCount

---

## 16. React Admin UI Requirements

### Theme

* Dark theme only for MVP
* High contrast
* Dense admin layout
* Responsive enough for desktop and tablet

### Pages

#### Login

* Email/username
* Password
* Remember me

#### Dashboard

* Server health
* Library root count
* Track count
* Album count
* Artist count
* Current scan status
* Recent scan jobs

#### Library Roots

* List configured roots
* Add/edit/delete roots
* Enable/disable root
* Trigger scan
* View last scan outcome

#### Users

* List users
* Create user
* Edit role
* Enable/disable
* Reset password
* Delete user

#### Music Library

* Browse artists
* Browse albums
* Browse tracks
* Search
* Inspect metadata
* Inspect artwork

#### Playlists

* Create/edit/delete playlists
* Add/remove tracks
* Reorder tracks

#### System

* App version
* Config summary
* Storage paths status
* Logs summary

### UI Tech Requirements

* React + TypeScript
* Strong typing for API DTOs
* Shared API client layer
* Auth interceptor for access token / refresh flow
* TanStack Query for remote data
* Route guards for admin-only pages

---

## 17. Recommended Backend Interfaces

These interfaces must exist so the system does not collapse into controller spaghetti.

```csharp
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}

public interface ITokenService
{
    string CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);
    string CreateRefreshToken();
    string HashRefreshToken(string refreshToken);
}

public interface ILibraryRootService
{
    Task<IReadOnlyList<LibraryRootDto>> GetAllAsync(CancellationToken ct);
    Task<Guid> CreateAsync(CreateLibraryRootRequest request, CancellationToken ct);
    Task UpdateAsync(Guid id, UpdateLibraryRootRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public interface IFileScanner
{
    IAsyncEnumerable<DiscoveredAudioFile> EnumerateAudioFilesAsync(string rootPath, CancellationToken ct);
}

public interface IMetadataReader
{
    Task<AudioMetadata> ReadAsync(string filePath, CancellationToken ct);
}

public interface IArtworkService
{
    Task<ArtworkResult?> ExtractAndStoreAsync(AudioMetadata metadata, CancellationToken ct);
    Task<FileStreamResultModel?> OpenArtworkAsync(Guid artworkId, CancellationToken ct);
}

public interface ILibraryIndexService
{
    Task<ScanJobDto> ScanLibraryRootAsync(Guid libraryRootId, CancellationToken ct);
}

public interface IStreamingService
{
    Task<StreamableTrackResult> GetStreamableTrackAsync(Guid trackId, CancellationToken ct);
}

public interface IPlaylistService
{
    Task<Guid> CreateAsync(CreatePlaylistRequest request, CancellationToken ct);
    Task AddTrackAsync(Guid playlistId, Guid trackId, CancellationToken ct);
    Task ReorderAsync(Guid playlistId, ReorderPlaylistItemsRequest request, CancellationToken ct);
}
```

---

## 18. Database Strategy

### MVP Choice

Use **SQLite** for MVP because:

* Simple deployment
* Works well in Docker with a persistent volume
* Good enough for a personal server MVP

### Future Upgrade Path

Abstract database configuration so the app can move to PostgreSQL later without rewriting the application layer.

### EF Core Rules

* Use migrations
* Configure indexes on common search fields
* Use explicit entity configurations
* Avoid lazy loading
* Use `AsNoTracking()` for read-heavy queries

### Suggested Indexes

* `Track.FilePath`
* `Track.Title`
* `Artist.Name`
* `Album.Title`
* `Track.AlbumId`
* `Track.PrimaryArtistId`
* `Playlist.OwnerUserId`
* `LibraryRoot.Path`

---

## 19. Search Requirements

### MVP Search

Support text search over:

* Track title
* Artist name
* Album title
* Genre

### Search Behavior

* Case-insensitive
* Prefix and contains matching
* Paged results
* Grouped result types if helpful

### Later

* Full-text search index
* Fuzzy search
* Multi-field ranking

Keep MVP simple.

---

## 20. Artwork Storage Strategy

### Rules

* Store artwork files under server-managed storage
* Use deterministic names or hashed names
* Never trust client-provided file extensions blindly
* Save mime type and dimensions in DB

### Example Path Pattern

```text
/app/data/artwork/{first-two-hash-chars}/{full-hash}.jpg
```

### Why

* Avoid duplicates
* Avoid giant flat folders
* Serve stable URLs through API

---

## 21. Logging and Diagnostics

### Log Requirements

* Application startup/shutdown
* Auth success/failure
* Scan start/finish/failure
* File read errors
* Metadata parse failures
* Stream access failures

### Diagnostics Requirements

* Health endpoint
* Current config summary without exposing secrets
* Last scan stats
* Count summaries

---

## 22. Validation Rules

### Library Roots

* Path must be container-visible
* Path must exist at validation time if configured to validate immediately
* Path must not duplicate another library root

### Users

* Unique username/email
* Password policy from Identity
* Disabled users cannot log in

### Playlists

* Name required
* Owner required unless system playlist
* Playlist item reordering must preserve unique order values

---

## 23. Error Handling Rules

* Use centralized exception handling middleware
* Return RFC-style problem details
* Never return raw stack traces outside development
* Scan failures should be recorded per job
* A bad audio file should not crash the whole scan

---

## 24. Performance Rules

* Do not read all metadata into memory at once for huge libraries
* Scan in batches where practical
* Use async file I/O where sensible
* Use projection queries for list endpoints
* Cache simple counts if necessary, but do not over-engineer caching in MVP

---

## 25. Testing Requirements

### Unit Tests

* Metadata normalization
* Playlist ordering logic
* Token hashing and refresh logic
* Search filtering
* Library root validation

### Integration Tests

* Auth flows
* User CRUD
* Library root CRUD
* Scan endpoint + DB side effects
* Stream endpoint headers and range support

### Manual Test Cases

* Index a mounted music library
* Re-scan after adding a new file
* Re-scan after deleting a file
* Stream a track and seek forward
* Login as admin and create a user
* Login as regular user and verify permissions

---

## 26. Security and Hardening Notes

* Keep music library mounts read-only in Docker unless there is a real reason not to
* Never allow arbitrary path browsing from API inputs
* Validate every library path against configured roots
* Do not expose UNC paths, host paths, or local absolute server paths in API responses
* Rate-limit auth endpoints later if this moves beyond personal LAN use

---

## 27. Delivery Order

Build in this order.

### Phase 1 - Skeleton

* Solution structure
* Domain/Application/Infrastructure/API projects
* EF Core + Identity + SQLite
* Auth endpoints
* Dockerfile + compose
* Dark React shell + login

### Phase 2 - Library Roots and Scan Engine

* Library root CRUD
* File enumeration
* Metadata extraction
* Scan jobs
* Dashboard scan status

### Phase 3 - Library Browse

* Artists/albums/tracks endpoints
* Search endpoint
* Artwork extraction + serving
* Library browse screens

### Phase 4 - Streaming

* Stream endpoint with range support
* Track detail DTOs with stream URLs
* Manual playback verification from browser/client

### Phase 5 - Playlists and Polish

* Playlist CRUD
* Reordering
* Better dashboard
* Diagnostics page
* Error handling polish

---

## 28. Acceptance Criteria for MVP

The MVP is considered complete when all of the following are true:

* Admin can log in and manage users
* Admin can configure at least one mounted library path
* System can scan that path and index supported audio files
* Indexed library data is stored in SQLite on a persistent volume
* Artwork is extracted and stored on the server
* Admin UI can browse artists, albums, tracks, and playlists
* Search works across core music fields
* Authenticated clients can stream indexed tracks with seek support
* App runs in Docker with persistent data volumes
* The design does not rely on raw UNC paths inside the container runtime

---

## 29. Explicit Guidance for the Coding Agent

### Build Rules

* Use ASP.NET Identity; do not hand-roll password security
* Use EF Core migrations
* Use DTOs and mapping logic explicitly
* Use constructor injection everywhere
* Use interfaces for scanning, metadata, artwork, streaming, auth token work
* Keep controllers thin
* Keep file system access in Infrastructure
* Keep UI dark themed from the start

### Avoid These Mistakes

* Do not put all code in one API project with god classes
* Do not use a fake generic repository for everything
* Do not expose server file paths to the client
* Do not assume the container can directly access `\\192.168.0.106\music`
* Do not buffer full media files in memory for streaming
* Do not make file watchers the primary sync mechanism for network shares
* Do not store artwork blobs directly in SQLite for MVP unless absolutely necessary

---

## 30. Suggested First Environment Configuration

### Development Assumptions

* Host machine has access to `\\192.168.0.106\music`
* Host mounts that source into Docker as `/music/library-main`
* ASP.NET API runs on port 8080
* React admin UI is served by the API host in production mode
* SQLite file lives in `/app/data/db/sonafly.db`

### Initial Seed Data

Seed:

* One admin user
* Roles: `Admin`, `User`
* Optional default library root named `Main Library`

---

## 31. Future-Ready Extensions After MVP

Not for first delivery, but design so these can be added later.

* Transcoding service with FFmpeg
* Waveform generation
* Last played / play history
* Favorites
* Smart playlists
* Multi-artwork variants
* External metadata enrichment
* Android-specific optimized endpoints
* WebSocket or SignalR progress updates for scans

---

## 32. Final Build Summary

SonaFly MVP should be built as a **clean, container-friendly, single-server music platform** with:

* ASP.NET backend
* React dark-themed admin UI
* SQLite persistence
* Identity-based user management
* Mounted music library roots
* Metadata extraction
* Artwork caching
* Playlist support
* Authenticated range-based audio streaming

The implementation must prioritize correctness, separation of concerns, and a path to growth without turning the codebase into a mess.
