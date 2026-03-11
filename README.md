# 🎵 SonaFly

**A self-hosted music streaming server with a web admin UI and cross-platform MAUI mobile app.**

SonaFly lets you stream your personal music library from anywhere. Point it at your music folders, and it automatically indexes your collection with metadata, artwork, playlists, and more.

---

## Features

- 🎶 **Stream** MP3, FLAC, M4A, AAC, OGG, Opus, and WAV files
- 📱 **Cross-platform app** — Android & iOS via .NET MAUI
- 🌐 **Web admin panel** — manage your library, users, and settings from any browser
- 🎧 **Auditorium** — shared listening rooms where everyone hears the same music in sync
- 📋 **Playlists & Mixed Tapes** — create and manage custom playlists
- 🔍 **Search** — full-text search across tracks, albums, and artists
- 🖼️ **Automatic artwork** — extracts embedded art and fetches from MusicBrainz
- 👥 **Multi-user** — role-based access with admin and user roles
- 🚫 **Content restrictions** — admins can restrict content per user
- 🐳 **Docker-ready** — single container deployment

---

## Quick Start (Docker)

### 1. Clone the repository

```bash
git clone https://github.com/your-username/SonaFly.git
cd SonaFly
```

### 2. Create your `.env` file

```bash
cd docker
cp .env.example .env
```

Edit `.env` with your settings:

```env
# REQUIRED: JWT signing secret (minimum 32 characters)
JWT_SECRET=your-generated-secret-here

# Default admin password (only used on first startup)
ADMIN_DEFAULT_PASSWORD=Admin123!

# Database and artwork paths (inside container)
DB_CONNECTION=Data Source=/app/data/db/sonafly.db
ARTWORK_ROOT=/app/data/artwork
```

### 3. Set your JWT Secret

> ⚠️ **IMPORTANT**: You **must** change the `JWT_SECRET` in `.env` before deploying.

This secret is used to sign authentication tokens. It must be:
- **At least 32 characters long**
- **Random and unique** to your deployment
- **Kept private** — do not commit it to version control

Generate one with:

```bash
# Linux/macOS
openssl rand -base64 48

# PowerShell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

Then set it in your `.env` file:

```env
JWT_SECRET=your-generated-secret-here
```

> 💡 **Safety net**: SonaFly will **refuse to start** in Production mode if the default dev secret is still in use.

### 4. Mount your music library

Replace `/path/to/your/music` with the path to your music files on the host machine:

```yaml
volumes:
  - /mnt/nas/music:/music/library-main:ro
```

**Multiple libraries** — add more volume mounts:

```yaml
volumes:
  - /mnt/nas/rock:/music/rock:ro
  - /mnt/nas/jazz:/music/jazz:ro
  - /home/user/local-music:/music/local:ro
```

**Network shares (SMB/CIFS)** — mount to the host first, then bind-mount into the container:

```bash
# On the Docker host
sudo mount -t cifs //server/music /mnt/music -o username=user,password=pass,uid=1000

# Then in docker-compose.yml
volumes:
  - /mnt/music:/music/library-main:ro
```

> 💡 Add your CIFS mount to `/etc/fstab` for persistence across reboots.

### 5. Build and run

```bash
cd docker
docker compose up -d --build
```

SonaFly will be available at **http://your-server:8080**

---

## Default Admin Account

On first startup, SonaFly automatically creates an admin account:

| Field    | Value         |
|----------|---------------|
| Username | `admin`       |
| Password | `Admin123!`   |

> ⚠️ **Change this password immediately** after your first login via the web UI (Users → Edit admin user).

---

## First-Time Setup

1. **Log in** at `http://your-server:8080` with the default admin credentials
2. **Add Library Roots** — go to **Library Roots** and add your music paths using the **container-side paths** (e.g., `/music/library-main`)
3. **Scan** — click Scan on each library root to index your music
4. **Create users** — go to **Users** to add accounts for your listeners
5. **Create Auditoriums** — go to **Auditoriums** to set up shared listening rooms (optional)

---

## Mobile App (MAUI)

The SonaFly MAUI app connects to your server and provides:

- Browse artists, albums, and tracks
- Search your library
- Stream music with full playback controls
- Add to playlists with the ＋ button
- Join Auditorium listening rooms

### Connecting the App

On first launch, enter your server URL:

```
http://your-server:8080
```

Then log in with your username and password.

---

## Configuration Reference

All configuration is done via environment variables in `docker-compose.yml`:

| Variable | Description | Default |
|----------|-------------|---------|
| `Jwt__Secret` | **Required.** Signing key for auth tokens (min 32 chars) | Dev key (insecure) |
| `Jwt__Issuer` | Token issuer name | `SonaFly` |
| `Jwt__Audience` | Token audience | `SonaFlyClients` |
| `Jwt__AccessTokenExpirationMinutes` | Access token lifetime | `30` |
| `Jwt__RefreshTokenExpirationDays` | Refresh token lifetime | `7` |
| `ConnectionStrings__DefaultConnection` | SQLite database path | `Data Source=/app/data/db/sonafly.db` |
| `SonaFly__ArtworkRoot` | Directory for cached artwork | `/app/data/artwork` |
| `ASPNETCORE_URLS` | Listen URL | `http://+:8080` |

---

## Data Persistence

All persistent data is stored in Docker volumes:

| Volume | Contents |
|--------|----------|
| `sonafly_db` | SQLite database (users, library index, playlists) |
| `sonafly_artwork` | Cached album artwork |
| `sonafly_logs` | Application logs |

**Backup** — back up the `sonafly_db` volume to preserve your library index, users, and playlists:

```bash
docker compose cp sonafly:/app/data/db/sonafly.db ./sonafly-backup.db
```

---

## Supported Audio Formats

| Format | Extension | MIME Type |
|--------|-----------|-----------|
| MP3 | `.mp3` | `audio/mpeg` |
| FLAC | `.flac` | `audio/flac` |
| AAC / M4A | `.m4a`, `.aac` | `audio/mp4` |
| OGG Vorbis | `.ogg` | `audio/ogg` |
| Opus | `.opus` | `audio/ogg` |
| WAV | `.wav` | `audio/wav` |

---

## Auditorium (Shared Listening)

Auditoriums are shared listening rooms powered by SignalR. Key rules:

- **Admins** create and delete auditoriums (via web UI → Auditoriums)
- **Anyone** can join a room and queue songs (max 100 in queue)
- **No individual controls** — no pause, skip, or seek. Everyone hears the same stream.
- **Auto-sync** — joining mid-song syncs to the current position (~1 second accuracy)
- **Auto-pause** — music pauses when the room empties and resumes when someone joins

---

## Troubleshooting

### Dashboard shows "Scan Status: Running" when nothing is scanning
This happens if the server was restarted during a scan. The stale jobs reset automatically on the next server start.

### Can't access from mobile app
- Ensure port `8080` is open on your firewall
- Use `http://` (not `https://`) unless you've configured TLS
- The app and server must be on the same network (or use port forwarding / reverse proxy)

### Music files not found after scan
- Verify the volume mount path in `docker-compose.yml`
- Library roots in the admin UI must use **container-side paths** (e.g., `/music/library-main`), not host paths
- Check file permissions — the container runs as a non-root user

---

## Tech Stack

- **Backend**: ASP.NET Core 10, Entity Framework Core, SQLite, SignalR
- **Web UI**: React, TypeScript, Material UI, React Query
- **Mobile App**: .NET MAUI (Android & iOS)
- **Container**: Docker (Debian-based .NET runtime)

---

## License

This software is provided free of charge for personal and non-commercial use.

---

Made with ♪ by SonaFly
