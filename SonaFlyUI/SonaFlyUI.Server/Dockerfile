# Base images are pinned to a minor version rather than a floating major, so a rebuild
# does not silently move to a new runtime. Review and bump these deliberately (and
# rebuild) when a .NET patch release ships; see "Updating" in README.md.
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Install Node.js for React build
RUN apt-get update && apt-get install -y curl && \
    curl -fsSL https://deb.nodesource.com/setup_22.x | bash - && \
    apt-get install -y nodejs && \
    rm -rf /var/lib/apt/lists/*

# Copy project files
COPY SonaFlyUI/SonaFlyUI.Server/SonaFlyUI.Server.csproj SonaFlyUI/SonaFlyUI.Server/
COPY SonaFlyUI/sonaflyui.client/package*.json SonaFlyUI/sonaflyui.client/
COPY SonaFlyUI/sonaflyui.client/sonaflyui.client.esproj SonaFlyUI/sonaflyui.client/

# Restore
WORKDIR /src/SonaFlyUI/sonaflyui.client
RUN npm ci

WORKDIR /src/SonaFlyUI/SonaFlyUI.Server
RUN dotnet restore

# Copy all source
WORKDIR /src
COPY SonaFlyUI/ SonaFlyUI/

# Build React client
WORKDIR /src/SonaFlyUI/sonaflyui.client
RUN npm run build

# Publish .NET
WORKDIR /src/SonaFlyUI/SonaFlyUI.Server
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# curl is needed by HEALTHCHECK below. The previous Dockerfile installed it only in the
# build stage, which is discarded, so the health probe could never run and the container
# reported "unhealthy" forever. The slim aspnet image ships no HTTP client of its own.
#
# libchromaprint-tools provides fpcalc for music identification fingerprints; it pulls in
# the FFmpeg decoding libraries it needs. The version comes from the base image's
# distribution and is logged per fingerprint, so a rebuild that moves it stays visible.
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl libchromaprint-tools && \
    rm -rf /var/lib/apt/lists/* && \
    fpcalc -version

COPY --from=build /app/publish .

# Copy React build output to wwwroot
COPY --from=build /src/SonaFlyUI/sonaflyui.client/dist ./wwwroot

# The container parses media supplied from mounted storage, so it does not run as root.
# APP_UID is the non-root user the .NET base images provide. Only the data directories
# are writable; the application directory stays owned by root and read-only to the app.
RUN mkdir -p /app/data/db /app/data/artwork /app/data/logs /app/data/keys && \
    chown -R $APP_UID:$APP_UID /app/data

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__DefaultConnection="Data Source=/app/data/db/sonafly.db"
ENV SonaFly__ArtworkRoot=/app/data/artwork
# Signs stream tickets. Mount a persistent volume here, or every container replacement
# invalidates the stream URLs clients are currently playing.
ENV SonaFly__DataProtectionKeyRoot=/app/data/keys
# Bundled fingerprinting tool. Identification itself stays off until
# SonaFly__Identification__Enabled=true is set for the container.
ENV SonaFly__Identification__FpcalcPath=/usr/bin/fpcalc

EXPOSE 8080

USER $APP_UID

# Fails once the database is unreachable, not merely when the process stops answering.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "SonaFlyUI.Server.dll"]
