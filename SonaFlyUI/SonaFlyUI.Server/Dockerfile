FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create data directories
RUN mkdir -p /app/data/db /app/data/artwork /app/data/logs

COPY --from=build /app/publish .

# Copy React build output to wwwroot
COPY --from=build /src/SonaFlyUI/sonaflyui.client/dist ./wwwroot

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__DefaultConnection="Data Source=/app/data/db/sonafly.db"
ENV SonaFly__ArtworkRoot=/app/data/artwork

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "SonaFlyUI.Server.dll"]
