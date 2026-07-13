# ─── Build stage ────────────────────────────────────────────────────────────
# Flat single-project restore (not the Clean Architecture multi-project pattern used by
# AuctionService/SearchService/BiddingService) — but unlike GatewayService, NotificationService
# does reference the shared Contracts project (it consumes MassTransit events published there).
# Copy the shared Directory.Build.props (TargetFramework / Nullable / TreatWarningsAsErrors)
# plus both csproj files before the rest of the source, so `dotnet restore` lands in its own
# cached layer and only re-runs when a project file or dependency changes — not on every
# source edit.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Shared MSBuild props inherited by all backend projects — required for restore.
COPY Directory.Build.props ./

# Project files first (paths are relative to the `backend/` build context).
COPY Contracts/Contracts.csproj Contracts/
COPY NotificationService/NotificationService.csproj NotificationService/

# Restores the project and, transitively, the referenced Contracts project.
RUN dotnet restore NotificationService/NotificationService.csproj

# Copy the remaining source and publish (restore already done → --no-restore).
COPY Contracts/ Contracts/
COPY NotificationService/ NotificationService/
RUN dotnet publish NotificationService/NotificationService.csproj \
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

# Run as the non-root user baked into the .NET base image (UID 1654).
USER $APP_UID

# Kestrel listens on 8080 by default in the .NET container images (non-privileged,
# so it works under the non-root user above).
EXPOSE 8080

ENTRYPOINT ["dotnet", "NotificationService.dll"]
