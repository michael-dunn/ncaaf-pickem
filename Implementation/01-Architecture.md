# 01 - Architecture

Decisions here were made to fit the stories' constraints: .NET with minimal endpoints, SQL Server already on the home server, single deployable unit, mobile-first PWA, no email, near-zero cost. Change them only via `DECISIONS.md`.

## Stack

| Concern | Decision | Why |
|---|---|---|
| Runtime | .NET 10 (LTS), C# 14 | "Latest dotnet" per Feature 10. |
| HTTP API | ASP.NET Core minimal APIs, grouped with `MapGroup` per feature, `TypedResults` | Feature 10 asks for minimal endpoints. |
| Frontend | Blazor WebAssembly PWA, hosted by the API project (one deployable) | C# end to end, shared DTOs, PWA template with manifest and service worker. See load-time risk below. |
| Persistence | EF Core 10 + SQL Server, code-first migrations | SQL Server exists on the host. Migrations are agent-friendly. |
| Auth | ASP.NET Core cookie auth + `Microsoft.AspNetCore.Authentication.Google`, no Identity | Feature 08 Option A, decided. |
| Background jobs | One `BackgroundService` scheduler using Cronos for cron expressions, plus a dedicated adaptive Saturday poller | Feature 10: jobs run inside the app. No Hangfire/Quartz, to keep schema and ops surface small. |
| Time | `TimeZoneInfo.FindSystemTimeZoneById("America/New_York")`, all storage UTC | Feature 13. .NET on Windows resolves IANA IDs since .NET 6. |
| Web push | `WebPush` NuGet (web-push-libs), VAPID keys from config | Feature 11. |
| Reference data | CollegeFootballData via official `CollegeFootballData` NuGet client | Feature 12, decided. |
| Live scores | ESPN unofficial scoreboard via `HttpClient`; CFBD games endpoint as fallback | Feature 12, decided. |
| Tests | xUnit, FluentAssertions, `WebApplicationFactory` for API tests, real SQL Server (LocalDB or the host instance) for integration tests | Domain tests need no DB. API tests need real SQL Server because of SQL-specific behavior. |
| Logging | Serilog to rolling file + console | Simple to read on a home server. |

### Load-time risk on Blazor WASM

Feature 04 wants the picks page interactive within 2 seconds on mobile. Blazor WASM first load is several MB; repeat loads are cached and fast, and a home-screen app is almost always a repeat load. Mitigations are mandatory: IL trimming on, Brotli precompression, nothing lazy-loaded on the critical path, a branded loading splash.

**P0-04 includes a measured spike on a real phone over Tailscale.** If first load exceeds 5 seconds or repeat load exceeds 2 seconds, the fallback is Razor Pages with small vanilla JS modules for the picks page and dashboard polling. Log the switch in `DECISIONS.md` before any Phase 1 UI work starts.

## Solution layout

```
NcaafPickEm.sln
src/
  NcaafPickEm.Domain/          Entities, value objects, pure domain services. No EF, no HTTP.
    Leagues/  Seasons/  GameSets/  Points/  Picks/  Scoring/  Dashboard/  Leaderboard/
  NcaafPickEm.Shared/          DTOs and enums shared by Api and Web. No logic.
    Contracts/<Feature>/       Request/response records named exactly as in 03-API-Contracts.md
  NcaafPickEm.Infrastructure/  EF Core DbContext, migrations, repositories, providers, push, jobs.
    Data/  Providers/Cfbd/  Providers/Espn/  Providers/Fixture/  Push/  Jobs/  Services/
  NcaafPickEm.Api/             Program.cs, endpoint groups, auth, hosting of Web, health.
    Endpoints/<Feature>Endpoints.cs
    Auth/
  NcaafPickEm.Web/             Blazor WASM PWA. Pages, components, JS interop for push.
    Pages/<Feature>/  Components/  wwwroot/service-worker.js  wwwroot/manifest.webmanifest
tests/
  NcaafPickEm.Domain.Tests/    Pure unit tests. Must run in under 10 s with no DB.
  NcaafPickEm.Api.Tests/       Endpoint + DB integration tests with WebApplicationFactory.
  NcaafPickEm.Fixtures/        JSON fixtures: sample CFBD/ESPN payloads, a full sample week.
deploy/
  docker/
Implementation/  WorkItems/
```

Dependency direction: `Web -> Shared`; `Api -> Infrastructure -> Domain`; `Api -> Shared`; `Infrastructure -> Shared` (mapping only); `Domain -> Shared` (enums only, D-014; plus the `Contracts/Leaderboard` rows `StandingsCalculator` computes outright, D-139). `Shared` references nothing and holds no logic, so the graph stays acyclic and `Domain` still has no dependency on EF, HTTP, or a clock.

## Runtime shape

One process (`NcaafPickEm.Api`) on the home server. It serves the Blazor app's static files, the JSON API under `/api`, the auth endpoints under `/auth`, and hosts the job scheduler; SQL Server sits beside it.

**Primary target: Docker on a headless Linux host (P8-05, D-158).** The process runs in a container listening on plain HTTP on a loopback-only published port; `tailscale serve` terminates TLS on the host with a certificate Tailscale issues and renews for the machine's tailnet hostname, and forwards to it.

**`tailscale serve` also injects the identity headers the app signs people in from (P9-02, D-174).** Every forwarded request from a tailnet user carries `Tailscale-User-Login` and `Tailscale-User-Name`; the app's default scheme is a policy scheme that forwards to a `TailscaleAuthenticationHandler` whenever the login header is present, upserting `Users` by it and authenticating that one request. There is no session: the boundary is the loopback-only port publish plus the tailnet, and the header is trusted exactly as it arrives (Phase 9, Q4). A request without the header - a tagged device, a health probe, anything that did not come through Serve - is anonymous. SQL Server 2022 is a second container. `deploy/docker/compose.yaml` is the whole stack; a third container, `watchtower`, pulls a new image from GHCR and restarts the API container in place. Because the app then sees every request as coming from the Docker gateway over `http`, `App__BehindProxy=true` turns on `X-Forwarded-*` handling (D-160), `DataProtection__KeysPath` puts the cookie key ring on a volume so a redeploy does not sign everyone out (D-161), and `Database__MigrateOnStartup=true` makes the container migrate itself, since there is no separate deploy step (D-159).

```
Phone (home-screen PWA)
   |
   `--HTTPS over Tailscale--> tailscale serve (host :443)
                                  |
                                  `--HTTP--> 127.0.0.1:5000 -> ncaaf-api container :8080
                                                  |-- /            Blazor WASM static files
                                                  |-- /api/*       minimal endpoints (header identity)
                                                  |-- /auth/*      Google OAuth in/out (removed in P9-03)
                                                  |-- Scheduler    cron jobs (refresh, lock, reminders)
                                                  |-- SaturdayPoller  adaptive 5-min score polling
                                                  |-- /app/keys    data-protection key ring (volume)
                                                  |-- /app/logs    Serilog rolling file (volume)
                                                  `-- ncaaf-db container (SQL Server 2022) :1433
                                        outbound: CFBD API, ESPN scoreboard, Google OAuth, push services
```

## Key patterns

**Endpoint groups.** One static class per feature in `Api/Endpoints`, exposing `MapXxx(this RouteGroupBuilder)`. Each handler is a small method taking DTOs and services. No business logic in handlers; call an application or domain service.

**Application services.** Live in `Infrastructure/Services`. They load entities, call pure `Domain` functions, persist, and raise domain events. Domain functions are pure and unit-tested against fixtures.

**Domain events, in-process.** `GameAddedToSet`, `GameRemovedFromSet`, `WeekLocked`, `GameWentFinal`, `ResultOverridden`, `GameVoided`. Dispatched synchronously after `SaveChanges` through a small `IDomainEventDispatcher`. Notifications (Phase 7) and scoring (Phase 5) subscribe. No message bus.

**Authorization.** Three policies: `Authenticated`, `LeagueMember`, `LeagueCommissioner`. The league ID comes from the route (`{leagueId:guid}`). An endpoint filter loads the caller's membership once per request and stores it in `HttpContext.Items`. Feature 01 allows multiple commissioners: authorization checks `Membership.Role == Commissioner`, never a single `League.CommissionerId`.

**CSRF.** Cookie is `SameSite=Lax`, `HttpOnly`, `Secure`. All mutating `/api` calls must carry header `X-Requested-With: NcaafPickEm`; an endpoint filter rejects mutations without it. The Blazor `HttpClient` adds it through a delegating handler.

**Time.** Inject `TimeProvider`. Never call `DateTime.Now` or `DateTime.UtcNow` directly. `SeasonCalendar` (Domain) owns all Eastern-time logic.

**Provider isolation.** `IReferenceDataProvider` (teams, conferences, schedule, rankings, lines) and `ILiveScoreProvider` (scores and status for a date). Implementations: `CfbdReferenceDataProvider`, `EspnLiveScoreProvider`, `CfbdLiveScoreProvider` (fallback), and `Fixture*` versions. Everything else reads local tables only.

**Idempotency.** Every job and every scoring step is safe to re-run. Use natural keys (provider game ID, membership + game) and upserts. Cron runs are deduplicated by `JobRuns(JobName, ScheduledForUtc)`.

**Fixtures first.** `tests/NcaafPickEm.Fixtures` contains a full synthetic week (teams, schedule, rankings, lines, a Saturday score timeline) plus the Feature 05 worked example. `FixtureReferenceDataProvider` and `FixtureLiveScoreProvider` let the whole app run offline in Development. UI agents develop against fixtures, never live APIs.

## Configuration (environment variables / appsettings)

```
ConnectionStrings__Default
Google__ClientId, Google__ClientSecret
Cfbd__ApiKey
Providers__LiveScores = Espn | Cfbd | Fixture
Providers__ReferenceData = Cfbd | Fixture
Push__VapidPublicKey, Push__VapidPrivateKey, Push__Subject (mailto: or https:)
App__PublicOrigin = https://<tailnet-host>
Jobs__Enabled = true | false   (false in tests)
RateLimiting__Enabled, RateLimiting__AuthPermitPerMinute, RateLimiting__InvitePermitPerMinute (D-153)
```

Container deployment only (P8-05); every one is a documented no-op when unset:

```
App__BehindProxy = true | false          (default false; X-Forwarded-For/Proto/Host, D-160)
DataProtection__KeysPath = /app/keys     (unset = framework default; cookie key ring, D-161)
Serilog__LogDirectory = /app/logs        (unset = <content root>/logs)
Database__MigrateOnStartup = true        (D-015 default stays false; the compose opts in, D-159)
Database__StartupTimeoutSeconds = 120    (how long startup waits for SQL Server, D-159)
```

No secrets in the repo. `deploy/docker/.env.example` documents every key with a placeholder value, in the form the container deployment actually reads.
