# NcaafPickEm.Api - one container holding the API, the hosted Blazor WASM client, and the job
# scheduler (P8-05). SQL Server runs in its own container; see deploy/docker/compose.yaml.
#
# Build:  docker build -t ghcr.io/michael-dunn/ncaaf-pickem:latest .
# Run:    docker compose -f deploy/docker/compose.yaml up -d

# ---------------------------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from nothing but the project files, so an edit to a .cs file reuses this layer.
# global.json pins the SDK feature band; Directory.Build.props/Directory.Packages.props are
# repo-wide MSBuild imports that every csproj below depends on (Central Package Management means
# a missing Directory.Packages.props fails restore outright).
COPY global.json Directory.Build.props Directory.Packages.props NcaafPickEm.slnx ./
COPY src/NcaafPickEm.Api/NcaafPickEm.Api.csproj src/NcaafPickEm.Api/
COPY src/NcaafPickEm.Domain/NcaafPickEm.Domain.csproj src/NcaafPickEm.Domain/
COPY src/NcaafPickEm.Infrastructure/NcaafPickEm.Infrastructure.csproj src/NcaafPickEm.Infrastructure/
COPY src/NcaafPickEm.Shared/NcaafPickEm.Shared.csproj src/NcaafPickEm.Shared/
COPY src/NcaafPickEm.Web/NcaafPickEm.Web.csproj src/NcaafPickEm.Web/
COPY tests/NcaafPickEm.Api.Tests/NcaafPickEm.Api.Tests.csproj tests/NcaafPickEm.Api.Tests/
COPY tests/NcaafPickEm.Domain.Tests/NcaafPickEm.Domain.Tests.csproj tests/NcaafPickEm.Domain.Tests/
COPY tests/NcaafPickEm.Fixtures/NcaafPickEm.Fixtures.csproj tests/NcaafPickEm.Fixtures/

RUN dotnet restore src/NcaafPickEm.Api/NcaafPickEm.Api.csproj

COPY src/ src/
# Not a test dependency despite the path: NcaafPickEm.Infrastructure takes a real ProjectReference
# on NcaafPickEm.Fixtures for the embedded fixture JSON the Fixture providers and TeamAliasSeed
# read at runtime (P2-05). Without it the Infrastructure compile fails.
COPY tests/NcaafPickEm.Fixtures/ tests/NcaafPickEm.Fixtures/

# Publishing the Api publishes the referenced Blazor WASM client into wwwroot with the project's
# own Release settings (PublishTrimmed, Brotli). No TrimMode/AOT flags here on purpose: AOT needs
# the wasm-tools workload, and TrimMode=full is a documented publish trap (D-023..D-026).
RUN dotnet publish src/NcaafPickEm.Api/NcaafPickEm.Api.csproj \
        -c Release \
        -o /app/publish \
        --no-restore

# ---------------------------------------------------------------------------------------------
# Runtime stage
# ---------------------------------------------------------------------------------------------
# Debian-based aspnet image, deliberately: it ships tzdata and ICU, both of which this app needs.
# SeasonCalendar resolves "America/New_York" by name and the Api sets InvariantGlobalization=false
# (D-023) - the Alpine/`-composite` images would break both.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

LABEL org.opencontainers.image.source="https://github.com/michael-dunn/ncaaf-pickem"
LABEL org.opencontainers.image.title="NCAAF Pick Em"
LABEL org.opencontainers.image.description="Family college-football pick'em: API, hosted Blazor WASM PWA, and in-process job scheduler."
LABEL org.opencontainers.image.licenses="NOASSERTION"

# curl is here for HEALTHCHECK only; the aspnet image has no shell utility that can speak HTTP.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish ./

# The aspnet image already ships a non-root `app` user (uid 1654). Both writable paths are
# created and chowned while we are still root, so a *named* volume inherits the right ownership;
# a *bind* mount keeps the host's ownership, which is why the deploy docs tell the operator to
# `chown -R 1654:1654` these two directories on the server.
RUN mkdir -p /app/keys /app/logs && chown -R app:app /app/keys /app/logs
VOLUME ["/app/keys", "/app/logs"]

USER app

# /app/keys holds the data-protection key ring, so the auth cookie survives a redeploy;
# /app/logs holds Serilog's daily rolling file.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DataProtection__KeysPath=/app/keys \
    Serilog__LogDirectory=/app/logs

EXPOSE 8080

# /health/ready is anonymous, needs no CSRF header, and checks SQL Server, so an unhealthy
# database shows up as an unhealthy container. start-period covers the first-run migration.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "NcaafPickEm.Api.dll"]
