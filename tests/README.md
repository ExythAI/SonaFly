# Tests

## Prerequisites

The repository pins its SDK in [`global.json`](../global.json): **.NET 10** (feature band
`10.0.1xx` or newer). With an older SDK installed, `dotnet` refuses to build and names the
version it wanted, rather than failing later with a confusing target-framework error.

Web checks need **Node 22+**.

## Server and mobile tests

```sh
dotnet test tests/SonaFlyUI.Server.Tests/SonaFlyUI.Server.Tests.csproj -p:SkipSpaBuild=true
```

`SkipSpaBuild` drops the React project reference: these tests do not need the SPA
output, and building it here only makes the run slower.

Each fixture uses an in-memory SQLite database and, where a file is needed, a temporary
audio file. Nothing touches a configured music library.

| Suite | Covers |
|-------|--------|
| `AdminBootstrapTests` | The initial admin password: the published development credential is refused in Production, a blank or absent value generates a one-time password, weak values fail startup before any account exists |
| `SessionInvalidationTests` | Security-stamp checking: tokens issued before a disable, delete, password change, reset or role change stop validating; refresh-token families and replay detection |
| `UserAdministrationTests` | Role allowlisting, atomic role changes, Identity failures surfacing as errors, and the server always keeping one administrator who can sign in |
| `AlbumRestrictionTests` | Restricted content staying out of album and artist metadata and counts, not just out of the stream |
| `AuditoriumRestrictionTests` | Per-listener playability in a shared room, for artist, album and genre restrictions |
| `PlaybackTests` | Stream credentials and ticket scoping, queue restoration, concurrent queue updates, pause/resume timers, replay cancellation |
| `StreamTicketPersistenceTests` | Stream tickets surviving a container replacement, validating across replicas, and being rejected across deployments |
| `HealthCheckTests` | `/api/health` reporting unhealthy when the database is unreachable, rather than only when the process stops |
| `DeploymentTransportTests` | Forwarded-header trust configuration and the security response headers |
| `MobileClientTests` | Mobile token refresh and concurrency, keychain storage, and cross-server credential isolation |

The mobile HTTP sources (`SonaFlyApiClient`, `ServerStorageService`, `ServerConfig`) are
linked into this project and compiled unchanged, with test-only stand-ins for the MAUI
`Preferences` and `SecureStorage` APIs. That covers their HTTP and storage behaviour
without an Android build; **device playback still requires a MAUI build on a device**.

## Web checks

From `SonaFlyUI/sonaflyui.client`:

```sh
npm ci
npm test        # node --test for the API client, then Vitest for components
npm run typecheck
npm run lint    # --max-warnings 0: a new warning fails
npm run build
```

`npm test` runs two runners. `src/api/client.test.mjs` runs under `node --test` and covers
the axios layer: concurrent 401s sharing one refresh, a late 401 retrying an
already-rotated token, a refresh completing after logout being discarded, the refresh
token never being stored or sent in a body, and the one-time migration of a legacy
`localStorage` session to a cookie.

The remaining files run under Vitest with Testing Library:

- `auth/AuthContext.test.tsx` — session restore, logout clearing cached data and the
  in-memory token, and the refresh token never reaching browser storage.
- `components/PlayerContext.test.tsx` — a slow previous request not replacing the newly
  selected track, stop-during-load, queue replacement, duplicate tracks advancing by
  position, shared-room seeking, and retry after a streaming failure.
- `hooks/useSearchFilter.test.tsx` — search debouncing, pagination reset, and restoring
  the visible input on back navigation.

## What is *not* covered

There are **no browser or end-to-end tests**. Everything above is unit and integration
level, in-process. Playwright would be a reasonable addition — a real sign-in, a range
request against a silent WAV fixture, and a SignalR reconnect are the gaps worth closing
first — but none of it exists today.

There is also no automated coverage of:

- Android, iOS or MacCatalyst builds (CI builds only the MAUI Windows target).
- The TLS reverse-proxy deployment end to end. CI validates both compose files and
  builds the image, but does not stand up Caddy and exercise HTTPS.

## CI

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) runs all of the above on every
push and pull request: server build and tests, a check for EF model changes without a
migration, the web lint/typecheck/test/build set, a container image build whose health
check must report healthy and whose process must be non-root, compose validation, and
the MAUI Windows build.

## Streaming clients

Streaming requires a bearer token or a scoped URL from
`GET /api/stream/tracks/{id}/url`. Those URLs expire after two hours and re-check the
account, its security stamp, and content restrictions on every request. Anonymous URLs
do not work. Update the server, web client, and mobile client together.
