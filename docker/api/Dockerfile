# syntax=docker/dockerfile:1.7

# ─────────────────────────────────────────────────────────────────────────────
#  Multi-stage Dockerfile for Retail.Api.
#
#  WHY MULTI-STAGE?
#  ----------------
#  Stage 1 has the full .NET SDK (~1.2 GB) so it can restore + build.
#  Stage 2 has only the ASP.NET runtime (~220 MB) so the shipped image
#  carries no compilers, no NuGet cache, no source. Smaller image =
#  faster cold starts on Container Apps + smaller attack surface (no
#  build tooling for an attacker to leverage if they pop the container).
#
#  WHY THE TWO-STEP COPY (csproj first, then everything)?
#  ------------------------------------------------------
#  Docker layer caching: if no csproj or Directory.Build.props files
#  changed, the RUN dotnet restore layer is reused from cache even when
#  any .cs file changed. The full source copy comes AFTER restore, so a
#  source-only edit doesn't bust the NuGet cache layer. Cuts rebuild
#  time from ~90s to ~15s on incremental builds.
#
#  BUILD CONTEXT: repo root (not docker/api/). Required because we need
#  to pull in src/api/Retail.Api + src/ml/Retail.Ml + Directory.Build.props.
#  Build invocation: `docker build -f docker/api/Dockerfile .` from repo root.
# ─────────────────────────────────────────────────────────────────────────────

# Stage 1 — restore + build + publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files (and the central props) FIRST so restore is cacheable.
COPY ["Directory.Build.props", "./"]
COPY ["src/api/Retail.Api/Retail.Api.csproj", "src/api/Retail.Api/"]
COPY ["src/ml/Retail.Ml/Retail.Ml.csproj", "src/ml/Retail.Ml/"]

# Restore in a single RUN with --use-current-runtime to avoid pulling
# down extra RID-specific assets we'll never run.
RUN dotnet restore "src/api/Retail.Api/Retail.Api.csproj" --use-current-runtime

# Now bring in the actual sources.
COPY ["src/api/Retail.Api/", "src/api/Retail.Api/"]
COPY ["src/ml/Retail.Ml/", "src/ml/Retail.Ml/"]

# Publish in Release. --no-restore reuses the cached restore from above.
# Output to /app/publish so we can copy that single folder into stage 2.
WORKDIR "/src/src/api/Retail.Api"
RUN dotnet publish "Retail.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Stage 2 — minimal runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as a non-root user. The ASP.NET image already creates `app` (UID
# 1654) for exactly this purpose. Running as root in a container that's
# exposed to the network is a textbook footgun.
USER app

# Listen on 8080 (the convention for non-root .NET containers — port 80
# requires root). Container Apps and the docker-compose service map this
# to whatever host port we expose.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Development \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=true
EXPOSE 8080

# Pull the published bits over from the build stage. --chown so the
# `app` user can read them (would otherwise be owned by root from the
# build stage).
COPY --from=build --chown=app:app /app/publish .

# Healthcheck inside the container so Docker (and downstream services)
# know when we're actually ready. Hits the liveness endpoint we wired
# up in Story 0.2 — no DB dependency, no auth.
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD wget -q --spider http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Retail.Api.dll"]
