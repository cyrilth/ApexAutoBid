# ─── Build stage ────────────────────────────────────────────────────────────
# Flat single-project restore (not the Clean Architecture multi-project pattern used by
# AuctionService/SearchService) — IdentityService has no project references (not even to
# Contracts). Still copy the shared Directory.Build.props (TargetFramework / Nullable /
# TreatWarningsAsErrors) and the csproj before the rest of the source, so `dotnet restore`
# lands in its own cached layer and only re-runs when the project file or a dependency
# changes — not on every source edit.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Shared MSBuild props inherited by all backend projects — required for restore.
COPY Directory.Build.props ./

# Project file first (path is relative to the `backend/` build context).
COPY IdentityService/IdentityService.csproj IdentityService/

# Restores the project and its NuGet dependencies.
RUN dotnet restore IdentityService/IdentityService.csproj

# Copy the remaining source and publish (restore already done → --no-restore).
COPY IdentityService/ IdentityService/
RUN dotnet publish IdentityService/IdentityService.csproj \
    -c Release -o /app/publish --no-restore

# ─── Runtime stage ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is used by the docker-compose healthchecks (docker/docker-compose.yml probes
# /health/live and /health/ready from inside the container) -- the aspnet base image
# ships neither curl nor wget. Installed while this stage is still root; the
# USER $APP_UID switch below is unaffected.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# JSON console logging in the container (Requirements.md §13.5): the built-in
# JsonConsoleFormatter, enabled by configuration so platform log collectors can
# parse structured fields. Overridable per-environment via compose / k8s.
ENV Logging__Console__FormatterName=json

COPY --from=build /app/publish .

# Duende IdentityServer's Automatic Key Management persists its RS256 signing key to
# <current working directory>/keys (KeyManagementOptions.KeyPath defaults to
# Directory.GetCurrentDirectory(), not ContentRoot — they coincide here: /app/keys) — see
# backend/IdentityService/keys/ in local dev (gitignored). This is NOT a cosmetic persistence nicety: verified live that /app is
# root-owned in this base image, and without a writable keys/ directory, KeyManager's
# FileSystemKeyStore throws "Access to the path '/app/keys' is denied" the moment any
# endpoint needs key material (discovery, JWKS, token issuance) — a hard 500, not a graceful
# fallback to an ephemeral key. Create and chown the directory before dropping to the
# non-root user below so the app can actually write to it.
RUN mkdir keys && chown $APP_UID keys

# Run as the non-root user baked into the .NET base image (UID 1654).
USER $APP_UID

# Kestrel listens on 8080 by default in the .NET container images (non-privileged,
# so it works under the non-root user above).
EXPOSE 8080

ENTRYPOINT ["dotnet", "IdentityService.dll"]
