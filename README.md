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
git clone https://github.com/ExythAI/SonaFly.git
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

# Initial admin password (first startup only).
# Leave unset for production: a one-time password is generated and logged instead.
#ADMIN_DEFAULT_PASSWORD=

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

> 💡 **Safety net**: SonaFly will **refuse to start** in Production if the signing key is
> one that ships with this repository — the development secret in `appsettings.json` or
> the `CHANGE-ME-...` placeholder in `.env.example`. Those values are public, so anyone
> could forge an administrator token with them.

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

**Recommended — HTTPS via Caddy:**

```bash
cd docker
docker compose -f docker-compose.tls.yml up -d --build
```

Set `SONAFLY_DOMAIN` in `.env` first. Caddy obtains and renews a certificate
automatically, so the name must resolve publicly to this host with ports 80 and 443
reachable. SonaFly is then at **https://your-domain**, and HTTP redirects to HTTPS.
The application container is not published to the host at all in this setup.

**Trusted local network only — plain HTTP:**

```bash
cd docker
docker compose up -d --build
```

SonaFly will be available at **http://your-server:8080**. See
[Transport security](#transport-security) for what this exposes.

---

## Transport security

Everything that authenticates a user crosses the network on every request: the sign-in
password, the bearer token, the refresh cookie, the SignalR access token, and the
short-lived ticket in each stream URL. Over plain HTTP all of it is readable by anyone
on the network path. What that costs depends on which one is captured:

| Captured | What it grants |
|----------|----------------|
| Sign-in password | The account, indefinitely — until it is changed |
| Refresh cookie / refresh token | The account for the life of the token (7 days), renewable |
| Bearer / SignalR access token | The account until the token expires (15 minutes) |
| Stream ticket | One track, for that ticket's short lifetime. Not the API, and not the account |

Stream tickets are the narrowest of these by design, because they travel in a URL where
a browser or proxy may log them. The rest are account credentials, and a plain-HTTP
deployment hands them to anyone on the path.

**Use `docker-compose.tls.yml` for anything reachable beyond a network you fully
control.** Plain HTTP is for local development and trusted LANs only; Production logs a
warning at startup when TLS is not configured.

The bundled [Caddyfile](docker/Caddyfile) proxies everything as-is, so SignalR
WebSockets and range-based audio requests work without special casing, and it strips
`ticket` and `access_token` query values from its access log.

### Behind your own proxy

If you terminate TLS with nginx, Traefik or something else, set two things:

| Setting | Value |
|---------|-------|
| `SonaFly__Deployment__UseHttps` | `true` — enables the HTTP→HTTPS redirect, HSTS, and the `Secure` flag on the refresh cookie |
| `SonaFly__Deployment__KnownNetworks__0` | the CIDR your proxy talks from, e.g. `172.28.0.0/16` |

Use `SonaFly__Deployment__KnownProxies__0` instead if the proxy has a fixed address.

`X-Forwarded-Proto`, `X-Forwarded-For` and `X-Forwarded-Host` are honoured **only** from
those addresses. This matters: if every sender were trusted, any client could claim its
plain HTTP request arrived over TLS, or claim someone else's address and evade the
sign-in rate limit. Equally, if *no* proxy is declared the headers are ignored entirely,
the app believes every request is plain HTTP from the proxy's own address, and the
redirect and cookie flags never engage — so the setting is not optional when a proxy is
in use. A malformed address or CIDR fails startup rather than quietly falling back.

`HstsMaxAgeDays` defaults to 30. A browser honours HSTS for that long even if the
deployment later loses TLS, so raise it only once the setup is settled.

---

## Initial Admin Account

On first startup SonaFly creates a single `admin` account. There is **no universal
default password**.

### Production (recommended)

Leave `ADMIN_DEFAULT_PASSWORD` unset. SonaFly generates a random one-time password,
prints it **once** in the container log, and requires it to be changed at first login.

Read it back with the same compose file you started — `docker compose` defaults to
`docker-compose.yml` and will report no such service if you deployed the TLS stack:

```bash
# TLS deployment (docker-compose.tls.yml)
docker compose -f docker-compose.tls.yml logs sonafly | grep -A3 "one-time generated password"
```

```bash
# Plain-HTTP deployment (docker-compose.yml)
docker compose logs sonafly | grep -A3 "one-time generated password"
```

Until that password is changed, the account can only reach the change-password
endpoint — every other API call returns `403 password_change_required`.

If you would rather choose the password yourself, set `ADMIN_DEFAULT_PASSWORD` in
`.env` to a unique strong value. Production **refuses to start** if it is set to the
development credential below.

### Development

A local (non-Production) instance with no `ADMIN_DEFAULT_PASSWORD` set seeds the
admin account with the well-known development password `Admin123!` and logs a
warning. This credential is for local development only and is rejected in Production.

### How credentials are stored

| Client | Access token | Refresh token |
|--------|--------------|---------------|
| Web    | in memory only — gone when the tab closes | HttpOnly, SameSite=Strict cookie scoped to `/api/auth`; not readable by any script |
| Mobile | platform keychain (MAUI `SecureStorage`) | platform keychain |

Neither client writes a refresh token to `localStorage`, `sessionStorage` or app
preferences. Sessions created by earlier versions are migrated automatically on first
run — the old token is exchanged once and the plaintext copy deleted.

Because the web access token is held in memory, reloading the page re-obtains one from
the refresh cookie. The cookie is marked `Secure` whenever the request arrives over
HTTPS, so **serve the application over TLS** (see [Transport security](#transport-security));
over plain HTTP the cookie is still HttpOnly but travels in the clear.

### Sign-in protection

Five consecutive failed sign-ins lock an account for 15 minutes, and the
`/api/auth/login` and `/api/auth/refresh` endpoints are rate limited to 10 requests
per minute per source address.

---

## First-Time Setup

1. **Log in** at your server's address as `admin` (see [Initial Admin Account](#initial-admin-account) for the password)
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
https://your-domain
```

Then log in with your username and password. Use `http://your-server:8080` only on a
trusted local network — the app sends credentials and stream tickets on every request.

---

## Container behaviour

### Upgrading from a version that ran as root

Earlier images ran as root. Docker seeds a volume from the image only when the volume is
**empty**, so an existing `sonafly_db` (or `sonafly_keys`) volume keeps its root
ownership across an upgrade, and the non-root user the current image runs as cannot write
to it.

The failure is loud rather than subtle: startup applies pending migrations before the
application serves anything, so the container dies immediately with

```
Unhandled exception. Microsoft.Data.Sqlite.SqliteException (0x80004005):
SQLite Error 8: 'attempt to write a readonly database'.
   at ...Migrator.MigrateAsync(...)
```

Verified on a real upgrade from a March 2026 root-owned volume.

Fix the ownership once, with the application stopped:

```bash
cd docker
docker compose stop sonafly

# The uid the image runs as, rather than a number copied from somewhere.
APP_UID=$(docker compose run --rm --user root --entrypoint sh sonafly -c 'echo $APP_UID')

for volume in docker_sonafly_db docker_sonafly_keys docker_sonafly_artwork docker_sonafly_logs; do
  docker run --rm -v "${volume}:/data" alpine chown -R "$APP_UID:$APP_UID" /data
done

docker compose start sonafly
```

Compose prefixes volume names with the project directory, so the names above assume you
are running from `docker/`; `docker volume ls` shows the real ones. A fresh install needs
none of this.

### Health

`GET /api/health` is anonymous and reports database readiness, not just process
liveness:

```json
{ "status": "Healthy", "timestamp": "...", "checks": { "database": "Healthy" } }
```

It returns `503` when the database cannot be read, and the container's `HEALTHCHECK`
uses it, so a container whose database volume is missing or unreadable is marked
unhealthy instead of quietly accepting traffic. In the TLS compose file Caddy waits for
this check to pass before it starts proxying.

### Privileges and mounts

The application runs as the base image's non-root `app` user. Only `/app/data`
(database, artwork cache, logs) is writable; the application directory is not, and your
music library is mounted read-only — the container parses media from it but never needs
to write there.

### Stream tickets and the key ring

A stream URL carries a short-lived ticket signed with the ASP.NET Data Protection key
ring, scoped to one user and one track. Those keys live in the `sonafly_keys` volume at
`/app/data/keys`.

- **Keep that volume.** Deleting it invalidates every outstanding stream URL, cutting
  off whatever clients are currently playing. It is recreated automatically, but the old
  tickets stay dead.
- **Replicas must share it.** Two instances with separate key rings cannot validate each
  other's tickets, so a request that lands on the other replica fails.
- **Deployments are separated by their key rings, not by the application name.** The
  application name is fixed, so two SonaFly deployments pointed at the *same* key
  storage would accept each other's tickets. Give each deployment its own
  `sonafly_keys` volume — which separate compose projects do by default — and a ticket
  from one is meaningless to the other. The fixed name is what stops an unrelated
  application sharing that storage from minting tickets SonaFly would honour.

The keys are not encrypted at rest; they are protected by volume ownership, and the
container runs as a non-root user that only it can read them as. If your threat model
includes an attacker who can read the Docker volume, encrypt the underlying storage —
SonaFly deliberately does not require certificate management for a self-hosted setup.

If the key directory is missing or not writable, startup fails with an explicit error
rather than falling back to in-memory keys, which would look fine until the first restart.

### Updating

Base images are pinned by minor version via the `DOTNET_VERSION` build argument rather
than tracking a floating tag, so rebuilding does not silently move to a new runtime.
Bump it deliberately when a .NET release ships:

```bash
docker compose -f docker-compose.tls.yml build --build-arg DOTNET_VERSION=10.0 --pull
```

`--pull` refreshes the pinned tag to its latest patch build.

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
| `SonaFly__DataProtectionKeyRoot` | Directory for the stream-ticket key ring; must persist | `/app/data/keys` |
| `SonaFly__AdminDefaultPassword` | Initial admin password. Unset generates a one-time password | _(unset)_ |
| `SonaFly__Deployment__UseHttps` | Enable HTTPS redirection, HSTS and the `Secure` cookie flag | `false` |
| `SonaFly__Deployment__EnableHsts` | Send `Strict-Transport-Security` when `UseHttps` is on | `true` |
| `SonaFly__Deployment__HstsMaxAgeDays` | HSTS lifetime | `30` |
| `SonaFly__Deployment__KnownProxies__0` | Address of a trusted reverse proxy | _(none)_ |
| `SonaFly__Deployment__KnownNetworks__0` | CIDR of trusted reverse proxies | _(none)_ |
| `ASPNETCORE_URLS` | Listen URL | `http://+:8080` |

---

## Data Persistence

All persistent data is stored in Docker volumes:

| Volume | Contents |
|--------|----------|
| `sonafly_db` | SQLite database (users, library index, playlists) |
| `sonafly_keys` | Data Protection key ring that signs stream tickets |
| `sonafly_artwork` | Cached album artwork |
| `sonafly_logs` | Application logs |

`sonafly_artwork` is a cache and rebuilds itself. The other three are worth keeping.

### Backup

Copying `sonafly.db` out of a running container is **not** a backup. SQLite keeps recent
writes in a `-wal` file beside the main database, so a copy of `sonafly.db` alone can be
missing the newest data or be torn mid-transaction — and it will still open, which is
what makes the mistake expensive.

Stop the application and copy the whole set:

```bash
cd docker
docker compose stop sonafly
docker run --rm -v docker_sonafly_db:/db -v "$PWD:/out" alpine \
  sh -c 'cp /db/sonafly.db /db/sonafly.db-wal /db/sonafly.db-shm /out/ 2>/dev/null; cp /db/sonafly.db /out/'
docker compose start sonafly
```

(The `-wal` and `-shm` files exist only while the database has been written to since its
last checkpoint, hence the tolerated failure.)

To back up without stopping, use SQLite's own online backup, which produces a single
consistent file while the application keeps running. The runtime image does not ship the
`sqlite3` binary, so run it from a container that does, against the same volume:

```bash
docker run --rm -v docker_sonafly_db:/db -v "$PWD:/out" alpine \
  sh -c 'apk add --no-cache sqlite >/dev/null && \
         sqlite3 /db/sonafly.db ".backup /out/sonafly-backup.db"'
```

Back up the key ring the same way, and keep it with the database:

```bash
docker run --rm -v docker_sonafly_keys:/keys -v "$PWD:/out" alpine \
  sh -c 'cp -r /keys /out/sonafly-keys-backup'
```

> Volume names above carry Compose's project prefix — `docker_`, from the `docker`
> directory. `docker volume ls` shows the names on your host.

**Restoring** the database without its key ring leaves every outstanding stream URL
invalid — harmless, but clients playing at that moment stop. Restoring the key ring
without the database is worse: tickets validate against accounts that no longer match.
Restore the pair together.

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
- **No personal controls** — a listener cannot pause, seek, or skip for themselves.
  Everyone hears the same stream at the same position.
- **Skip and stop are shared controls**, not absent ones: whoever started the current
  track, and any admin, can skip or stop it for the whole room. Nobody else can.
- **Auto-sync** — joining mid-song syncs to the current position (~1 second accuracy)
- **Auto-pause** — music pauses when the room empties and resumes when someone joins

### Content restrictions in a room

Listeners in one room can have different restrictions, so a track one person is allowed
to queue may be blocked for another. The rule is:

- **What enters the room** is decided by the restrictions of whoever queues or starts the
  track. Someone restricted from a track cannot put it into the room at all.
- **The room keeps playing** for everyone else. One listener's restrictions never change
  what the room does, or the queue would behave differently depending on who is connected.
- **The restricted listener is told plainly** — they see "this track isn't available on
  your account" rather than a generic playback error, and rejoin automatically at the
  next track.

Stream access is enforced per listener regardless: a restricted listener's request for
the audio is refused by the server, so the notice is an explanation, not the control.

---

## Troubleshooting

### Dashboard shows "Scan Status: Running" when nothing is scanning
This happens if the server was restarted during a scan. The stale jobs reset automatically on the next server start.

### Can't access from mobile app
- **TLS deployment (recommended):** the app container publishes no port at all — Caddy
  does, on `443` (and `80`, for the redirect and certificate renewal). Open those, and
  enter `https://your-domain` in the app.
- **Plain-HTTP deployment:** open port `8080` and enter `http://your-server:8080`. Do
  this only on a network you fully control; see [Transport security](#transport-security).
- Check that the app and server can reach each other at all (same network, port
  forwarding, or a reverse proxy).

### Music files not found after scan
- Verify the volume mount path in `docker-compose.yml`
- Library roots in the admin UI must use **container-side paths** (e.g., `/music/library-main`), not host paths
- Check file permissions — the container runs as a non-root user

---

## Building and testing from source

| Requirement | Version |
|-------------|---------|
| .NET SDK | **10** — pinned in [`global.json`](global.json); an older SDK refuses to build and says so |
| Node.js | **22.18+** — the tests import `.ts` sources directly, which earlier 22.x cannot do |
| .NET MAUI workload | only to build the mobile app (`dotnet workload install maui`) |

```bash
# Server and mobile regression tests
dotnet test tests/SonaFlyUI.Server.Tests/SonaFlyUI.Server.Tests.csproj -p:SkipSpaBuild=true
```

```bash
# Web checks, from SonaFlyUI/sonaflyui.client
npm ci && npm run lint && npm run typecheck && npm test && npm run build
```

[`tests/README.md`](tests/README.md) describes what each suite covers — and what is
deliberately not covered. Every command above runs in CI
([`.github/workflows/ci.yml`](.github/workflows/ci.yml)), together with a container
image build whose health check must pass and whose process must be non-root.

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
