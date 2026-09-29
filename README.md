# NCAAF Pick Em

## 1. What it is

A mobile-first PWA for a family college-football pick-em league. Commissioners configure rules
that select Saturday FBS games each week and assign point values; members tap winners before the
first Saturday kickoff; at lock an influence dashboard shows each member which games matter most
against the rest of the league, with live scores. Games are scored automatically as they go final,
and weekly and season leaderboards update; web push reminds people to submit. A new member joins
by typing a six-digit code into the "Join by code" box on the home page, or by tapping an invite
link. The stack is .NET on a home server behind Tailscale, SQL Server, Tailscale identity headers,
CollegeFootballData for reference data and ESPN for live scores.

- Requirements: [`WorkItems/`](WorkItems) (13 feature stories — these are the spec).
- Implementation plan: [`Implementation/`](Implementation) — start at
  [`Implementation/00-README.md`](Implementation/00-README.md).
- Live task board: [`Implementation/STATUS.md`](Implementation/STATUS.md).
- Traceability (every acceptance criterion mapped to a proof): [`Implementation/07-Traceability.md`](Implementation/07-Traceability.md).
- Operator checklist (every manual, device-only verification, in order): [`Implementation/reviews/operator-checklist.md`](Implementation/reviews/operator-checklist.md).

## 2. Architecture summary

### Stack

| Concern | Decision | Why |
|---|---|---|
| Runtime | .NET 10 (LTS), C# 14 | "Latest dotnet" per Feature 10. |
| HTTP API | ASP.NET Core minimal APIs, grouped with `MapGroup` per feature, `TypedResults` | Feature 10 asks for minimal endpoints. |
| Frontend | Blazor WebAssembly PWA, hosted by the API project (one deployable) | C# end to end, shared DTOs, PWA manifest + service worker. |
| Persistence | EF Core 10 + SQL Server, code-first migrations | SQL Server exists on the host. Migrations are agent-friendly. |
| Auth | Tailscale Serve identity headers; per-request, no session | Phase 9: header trust is deliberate, the tailnet is the boundary. |
| Background jobs | One `BackgroundService` scheduler using Cronos for cron expressions, plus a dedicated adaptive Saturday poller | Feature 10: jobs run in-process. No Hangfire/Quartz. |
| Time | `TimeZoneInfo.FindSystemTimeZoneById("America/New_York")`, all storage UTC | Feature 13. |
| Web push | `WebPush` NuGet (web-push-libs), VAPID keys from config | Feature 11. |
| Reference data | CollegeFootballData via the official `CollegeFootballData` NuGet client | Feature 12. |
| Live scores | ESPN unofficial scoreboard via `HttpClient`; CFBD games endpoint as fallback | Feature 12. |
| Tests | xUnit, FluentAssertions, `WebApplicationFactory` for API tests, real SQL Server for integration tests | Domain tests need no DB; API tests need real SQL-specific behavior. |
| Logging | Serilog to rolling file + console | Simple to read on a home server. |

### Solution layout

```
NcaafPickEm.slnx
src/
  NcaafPickEm.Domain/          Entities, value objects, pure domain services.  -> Shared (enums only)
  NcaafPickEm.Shared/          DTOs and enums shared by Api and Web. No logic.
  NcaafPickEm.Infrastructure/  EF Core, migrations, providers, push, jobs.  -> Domain, Shared
  NcaafPickEm.Api/             Composition root, minimal endpoints, auth, hosts the Web app.
                                                                            -> Infrastructure, Shared, Web
  NcaafPickEm.Web/             Blazor WASM PWA.                             -> Shared
tests/
  NcaafPickEm.Domain.Tests/    Pure unit tests. No DB, no network, under 10 s.
  NcaafPickEm.Api.Tests/       WebApplicationFactory<Program> + real SQL Server.
  NcaafPickEm.Fixtures/        Embedded JSON fixtures plus `FixtureLoader`.
  NcaafPickEm.Simulator.Tests/ The simulator's engine and the app in Simulator mode, real SQL Server.
tools/
  NcaafPickEm.Simulator/       The simulator website (Blazor Server). Not part of the app or its image.
                                                                            -> Infrastructure
deploy/                        Deployment scripts and the production config template (P8-02).
Implementation/  WorkItems/    Plan and requirements.
```

Dependency direction is enforced by project references: `Web -> Shared`;
`Api -> Infrastructure -> Domain`; `Api -> Shared`; `Infrastructure -> Shared`;
`Domain -> Shared` (enums only — see D-014). `Shared` references nothing.

### Runtime shape

One process (`NcaafPickEm.Api`) on the home server. It serves the Blazor app's static files, the
JSON API under `/api`, the auth endpoints under `/auth`, and hosts the job scheduler; SQL Server
sits beside it. The primary deployment is Docker on a headless Linux box (P8-05, D-158): the
process runs in a container on plain HTTP on a loopback-only port, `tailscale serve` terminates
TLS on the host with the tailnet certificate, and SQL Server is a second container. The Windows
service from P8-02 is the alternative, and binds HTTPS itself with the Tailscale PEM pair.

```
Phone (home-screen PWA)
   |
   `--HTTPS over Tailscale--> tailscale serve (host :443)
                                  |
                                  `--HTTP--> 127.0.0.1:5000 -> ncaaf-api container :8080
                                                  |-- /            Blazor WASM static files
                                                  |-- /api/*       minimal endpoints (Tailscale header identity)
                                                  |-- /auth/dev-login  Development/Testing only
                                                  |-- Scheduler    cron jobs (refresh, lock, reminders)
                                                  |-- SaturdayPoller  adaptive 5-min score polling
                                                  |-- /app/keys    data-protection key ring (volume; dev-login cookie only)
                                                  |-- /app/logs    Serilog rolling file (volume)
                                                  `-- ncaaf-db container (SQL Server 2022) :1433
                                        outbound: CFBD API, ESPN scoreboard, push services
```

### Provider hybrid

Reference data (teams, conferences, schedule, rankings, lines) comes from the official CFBD client
by default (`Providers__ReferenceData=Cfbd`). Live scores default to ESPN's public scoreboard
(`Providers__LiveScores=Espn`); after 3 consecutive ESPN failures in a day, `CompositeLiveScoreProvider`
fails over to CFBD's games endpoint for the rest of that day and flags `ScoresMayBeStale` on the data
status page. Both switches accept `Fixture` too, which is what every dev/test run uses instead of a
live API — `FixtureReferenceDataProvider`/`FixtureLiveScoreProvider` replay the checked-in Week 7,
2026 sample week. Everything else in the app reads only local SQL tables; the two provider
interfaces (`IReferenceDataProvider`, `ILiveScoreProvider`) are the only place a live API is called.

### Jobs (all times Eastern; `Jobs__Enabled=false` disables the scheduler, e.g. in tests)

| Job | Schedule |
|---|---|
| `HeartbeatJob` | every 5 minutes (proof of life) |
| `TeamsRefreshJob` | Tue 03:00 |
| `ScheduleRefreshJob` / `ScheduleRefreshDailyJob` | Tue 03:10, then daily 04:00 Wed-Sat |
| `RankingsRefreshEveningJob` / `RankingsRefreshTuesdayJob` | Sun+Mon 20:00, then Tue 03:20 |
| `LinesRefreshJob` | daily 23:30 |
| `RegenerateGameSetsJob` | Tue 03:30 (auto-regenerate each league's week) |
| `EnsureCurrentWeekSetsJob` | Sun 00:05 (auto-create a set for a league with none) |
| `NightlyRescoreJob` | daily 04:30 |
| `LockWeekJob` | one-shot, due at each week's `LockAtUtc` (earliest Saturday kickoff) |
| `FridayMemberReminderJob` / `FridayCommissionerSummaryJob` | Fri 20:00 / Fri 21:00 |
| `SaturdayReminderOneShot` | one-shot, due at `LockAtUtc - 1h` |
| `PushRetryJob` | one-shot, +1/+5/+15 min after a failed push, then `Failed` |
| `SaturdayPoller` (a `BackgroundService`, not cron) | re-evaluates every minute during game day; polls live scores every 5 min (ESPN/Fixture) or 10 min (CFBD fallback) |
| `ReferenceDataBootstrapHostedService` (once, at startup) | on an empty database only: calendar, teams, then the current week's schedule/rankings/lines, so a fresh deployment does not wait for Tuesday |

## 3. Run locally

### Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.400 | Pinned in `global.json` (`rollForward: latestFeature`). |
| SQL Server | LocalDB or a full instance | LocalDB (`(localdb)\MSSQLLocalDB`) is enough for development and tests. |
| `dotnet-ef` | 10.0.x | `dotnet tool install --global dotnet-ef` — needed from P0-02 onwards. |

Optional: the `wasm-tools` workload. It is **not** required to build, run, or publish the Blazor
WebAssembly app; it is only needed if we ever turn on AOT (`RunAOTCompilation`). Trimming and
Brotli precompression work without it.

### Run with fixtures

```bash
dotnet run --project src/NcaafPickEm.Api
```

The `https` launch profile (the default) listens on `https://localhost:7092` and
`http://localhost:5204`, and already sets `Providers__ReferenceData=Fixture` and
`Providers__LiveScores=Fixture` so nothing calls a live API. To be explicit, or when running
without the profile:

```bash
Providers__ReferenceData=Fixture Providers__LiveScores=Fixture \
  dotnet run --project src/NcaafPickEm.Api --launch-profile https
```

On Windows PowerShell:

```powershell
$env:Providers__ReferenceData = "Fixture"; $env:Providers__LiveScores = "Fixture"
dotnet run --project src/NcaafPickEm.Api --launch-profile https
```

Probes: `GET /health` (liveness) and `GET /health/ready` (readiness; P0-02 adds the database
round-trip). Both return `{"status":"ok"}`.

The fixture data set (the "Week 7, 2026" sample week: 16 games, rankings, lines, and six
live-score snapshots from kickoff to all-Final) lives under
`tests/NcaafPickEm.Fixtures/Data/Week7_2026/`, loaded automatically at startup into an empty
database whenever `Providers:ReferenceData` is `Fixture`.

To get a signed-in demo league without a real tailnet, add:

```bash
Providers__ReferenceData=Fixture Providers__LiveScores=Fixture Seed__DemoLeague=true \
  dotnet run --project src/NcaafPickEm.Api --launch-profile https
```

```powershell
$env:Providers__ReferenceData = "Fixture"; $env:Providers__LiveScores = "Fixture"
$env:Seed__DemoLeague = "true"
dotnet run --project src/NcaafPickEm.Api --launch-profile https
```

This seeds the "Family League" demo league (members Michael, Alyson, Dance, Alex, Daniel; Michael
is Commissioner). Then visit <https://localhost:7092/auth/dev-login?user=michael> (or `alyson`,
`dance`, `alex`, `daniel`) to sign in as that member — no tailnet needed. Seeding and
dev-login are both Development/Testing only and never run in Production.

The fixture week is for automated tests and a quick look at the UI. To actually *use* the app -
pick, lock, watch the dashboard, score, run a season - on any day, use the simulator below.

### Simulating a week (`tools/NcaafPickEm.Simulator`)

The simulator (Phase 11, D-182..D-186) is a **separate website** that plays everything the app
does not own: the data provider (teams, the calendar, games, kickoffs, lines, the AP poll), the
scoreboard (live scores), the other players (their picks), and the scheduler's calendar ("it is
Friday at 8 PM"). It shares a database with a Development copy of the app and drives it through
the app's own services, so every rule behaves exactly as it does for real.

**Nothing moves the clock.** The app always runs on real time. Instead, the simulator moves the
*schedule*: "lock in 10 minutes" puts the week's first kickoff 10 minutes from now (the lock is the
first kickoff), and "next week" lays the season's calendar out so that week starts now. So a week
is never tied to a Saturday, the browser's countdown is always right, and a whole season plays out
in an afternoon.

Start both (Windows PowerShell, from the repo root):

```powershell
./tools/start-simulator.ps1
```

or by hand, in two terminals:

```bash
dotnet run --project src/NcaafPickEm.Api --launch-profile simulator   # the app, https://localhost:7092
dotnet run --project tools/NcaafPickEm.Simulator                       # the simulator, http://localhost:5299
```

In Visual Studio, pick the **App + Simulator** launch profile (`NcaafPickEm.slnLaunch`). Both use
the database `NcaafPickEm_Sim` on LocalDB and create it on first start. The app's `simulator`
launch profile is `Providers__ReferenceData=Simulator` and `Providers__LiveScores=Simulator`: in
that mode the app reads the calendar from the database and ingests and polls nothing of its own
(its data page says the data is "owned by NcaafPickEm.Simulator"), while its lock job, reminders
and every page work as normal. Both the mode and the simulator refuse to run outside Development.

Then, on the dashboard at <http://localhost:5299>:

1. **Create sandbox.** One click: 136 FBS teams (plus a few FCS), a calendar with week 1 current
   from now, a 14-game slate whose first kickoff is 15 minutes away, you (Michael) as
   commissioner, five players joined through real invites, Top 25 + SEC rules, close-line and
   conference point rules, and the week's set generated by the app's own Sunday job.
2. **Open the app** (top right) - it signs your browser in as you through `/auth/dev-login` and
   opens the app. Make your picks there.
3. **Pick for everyone else** from the league card: "Fill everyone" (strategies: mixed,
   favourites, underdogs, home teams, random; optionally leave one player incomplete), or click
   cells in the pick grid to pick for any player, and Submit / Clear per player. "Open as" signs the
   browser in as that player instead.
4. **Move the lock**: "Lock now", "in 2 min", "in 15 min", ... in the top bar. The whole slate
   shifts with it. The app's own lock job locks the week (the simulator runs it at once rather
   than waiting for the minute tick).
5. **Watch it play.** With autoplay on, every game plays out in real time after its kickoff, in 12
   minutes by default (set the length under "Autoplay & new weeks"), toward a target score drawn
   from the line. The dashboard updates live; scoring, the week leaderboard and standings follow.
   Or drive any game by hand: kick off, +7/+3, next quarter, final, set any score (a tie lands on
   the app's "needs review" list), postpone, cancel, reopen a final game. Change a target mid-game
   and it re-plans from the current score.
6. **Next week**, or **Play out N weeks** to build a season instantly (standings, weekly wins, trend
   arrows). **Rewind to before the lock** keeps every pick and plays the lock and the Saturday
   again; **Reset the week** starts it blank; **Before the season** / **Season over** test those
   states.

Also on the dashboard: add a game (any two teams, any kickoff, any line, Saturday or off-day),
nudge any kickoff, post or pull a line, flip a game's conference or Saturday flag, re-rank the AP
poll, add a player mid-season, join a player to a league you made in the app with its invite code,
and run any of the app's jobs now (Tuesday regeneration, the Friday reminders, the last call,
the nightly rescore). Every action's result is in the activity feed, including the app's own
refusals (a pick after the lock, a regenerate on a frozen week) - those are the point.

The simulator only touches a database it created: pointed at one with leagues or users but no
`sim.Settings` table (your Fixture dev database, say), it refuses to do anything. Reminder and
notification pushes it triggers use the app's VAPID pair (borrowed from the app's
`appsettings.Development.json` or user secrets); with none configured they are logged, not sent.
Not simulated: the ESPN-outage fallback ("scores may be stale" banner) - that state lives inside
the app's process.

### Local configuration

`src/NcaafPickEm.Api/appsettings.Development.json` is **gitignored**. Copy the committed template
and fill it in:

```bash
cp src/NcaafPickEm.Api/appsettings.Development.template.json \
   src/NcaafPickEm.Api/appsettings.Development.json
```

Every key can also be supplied as an environment variable with `__` as the separator. The full key
list lives in `Implementation/01-Architecture.md`:

```
ConnectionStrings__Default
Cfbd__ApiKey
Providers__LiveScores = Espn | Cfbd | Fixture
Providers__ReferenceData = Cfbd | Fixture
Push__VapidPublicKey, Push__VapidPrivateKey, Push__Subject
App__PublicOrigin = https://<tailnet-host>
Jobs__Enabled = true | false
```

No secrets go in the repo. `deploy/appsettings.Production.template.json` documents every key with a
placeholder for the home server.

### Running against real CFBD data

Reference data (teams, conferences, schedule, rankings, lines) can come from the live
CollegeFootballData API instead of the fixture set. Set `Providers__ReferenceData=Cfbd` and
`Cfbd__ApiKey=<your key>` (get a free-tier key at <https://collegefootballdata.com/key>);
`Providers__LiveScores` is independent and can stay `Fixture` or move to `Espn` on its own. Nothing
calls CFBD automatically — P2-04's jobs (and, until then, a manual call to
`ReferenceDataIngestService`) are what actually fetch and upsert data; see
`Implementation/AGENT-NOTES.md` ("Reference data ingest") for the service names and how ingest
failures are recorded on `GET /api/admin/data-status`.

### Web push (VAPID) keys

Web push needs a VAPID key pair. The app **boots fine without one** — `GET /api/push/vapid-public-key`
answers 503 and notifications are logged as Failed — so only set these when you want push to work.

```powershell
dotnet run --project src/NcaafPickEm.Api -- generate-vapid
```

or, against a running container image:

```bash
docker run --rm <image> generate-vapid
```

This is the Api's hidden `generate-vapid` argument, which prints a pair and exits without touching
the database or opening a port. Nothing is written to disk: paste the values into
`appsettings.Development.json`, user secrets, or the container's environment. `Push__Subject` must
be a real `mailto:` or `https:` contact — push services reject anything else.

**Keep the pair.** Replacing it invalidates every stored subscription, and every member has to turn
notifications on again. Never commit the private key.

### Notifications on iPhone

On iPhone/iPad, push only works from an app added to the Home Screen and opened from that icon
(iOS 16.4+); a regular Safari tab reports permission as denied and cannot receive push
(WorkItems/11-Notifications.txt). This has to be checked on a physical device — an operator step,
not something an agent in this environment can automate:

1. Set a real VAPID key pair on the server (`generate-vapid`, above) and confirm
   `GET /api/push/vapid-public-key` does not answer 503.
2. On the iPhone, joined to the tailnet, open the deployed app's URL in Safari — you land
   signed in as your Tailscale account, no login screen.
3. Tap the **Share** icon in Safari's toolbar, then **Add to Home Screen**.
4. Open the app from its new Home Screen icon, not from Safari — this is what makes
   `navigator.standalone` true and unlocks the Notifications section on `/me` (otherwise it shows
   the "add to Home Screen first" instructions with the "Turn on" action hidden).
5. On the Profile page, tap **Turn on notifications** and accept the permission prompt.
6. As a league commissioner, trigger a test push: either the Development/Testing-only "Send test
   notification" button (built with `DefineConstants=USE_FAKE_API` unset, in a Debug client build
   talking to a Development/Testing Api) or `POST /api/push/test` directly.
7. Confirm the notification banner appears, and that tapping it brings the standalone app to the
   foreground (or launches it) at the target page rather than opening a new Safari tab.

Record the result in `Implementation/STATUS.md`'s P7-02 row.

### Signing in locally

Identity comes from the `Tailscale-User-Login` header that `tailscale serve` injects on every
request (Phase 9, D-174). The handler is always on — there is no config flag to turn it on or
off — so there are two ways to get a signed-in session without a real tailnet:

1. **`GET /auth/dev-login?user=michael`** (or `alyson`, `dance`, `alex`, `daniel`) with
   `Providers__ReferenceData=Fixture` and `Seed__DemoLeague=true` set (see "Run with fixtures"
   above). This signs you in as that fixture member through the cookie scheme — the only scheme
   `/auth/dev-login` ever writes to — and is Development/Testing only; it is never mapped in
   Production. It also signs in the simulator's players (`Users.ExternalSubject = "sim:<name>"`,
   e.g. `?user=alyson`), which is what the simulator's "Open the app" and "Open as" links use.
2. **Send the headers yourself.** Since the Tailscale handler reads `Tailscale-User-Login` /
   `Tailscale-User-Name` on every request with no gating, a plain `curl` call or a
   header-modifying browser extension is a real, working sign-in:

   ```bash
   curl -H "Tailscale-User-Login: michael@example.com" -H "Tailscale-User-Name: Michael" \
     https://localhost:7092/api/me
   ```

   The first such request creates the `Users` row (`ExternalSubject` = the login verbatim,
   `Email` = the login, initial `DisplayName` from the RFC 2047-decoded name header); every later
   request with the same login matches the same user. There is no cookie and no session — every
   request is authenticated independently, and a missing or empty header is anonymous (401 under
   `/api`, the informational `/login` page elsewhere), never an error.

On the deployed server this same header is injected by `tailscale serve` itself, so a tailnet
member simply opens the app and is signed in with no login screen at all.

### Calling the API

- Unauthenticated `/api/*` returns **401 ProblemDetails**, never a redirect; the SPA handles it.
- Every mutating `/api` call must send `X-Requested-With: NcaafPickEm` or it is rejected with 400.
- League-scoped routes use `RequireLeagueMember()` / `RequireLeagueCommissioner()` on the group;
  the endpoint filter resolves `{leagueId}` once per request and handlers read it back with
  `HttpContext.GetMembership()`. Non-member is 404 (existence is not revealed); a member on a
  commissioner route is 403.

### A whole week, automated

`tests/NcaafPickEm.Api.Tests/Simulation/FullWeekSimulationTests.cs` walks one league through a full
fixture week - invites, regeneration, picks, both Friday reminders, the last call, the lock, the
Overview dashboards, all six score snapshots, a void, an override, the leaderboards - and is the
executable description of a normal week. `tests/NcaafPickEm.Simulator.Tests` does the same with the
simulator driving the real app in Simulator mode, on a Tuesday. The manual iPhone walkthrough is
`Implementation/screenshots/e2e/README.md`, driven from the simulator's dashboard.

## 4. Run tests

```bash
dotnet test
```

- `NcaafPickEm.Domain.Tests` is pure: no database, no network.
- `NcaafPickEm.Simulator.Tests` covers the simulator: its pure pieces (calendar layout, score plans,
  slates), its engine end to end against a throwaway database per test (`NcaafPickEm_SimTest_<guid>`,
  same `TEST_SQL_CONNECTION` seam), and the real app booted in Simulator mode over the database the
  simulator wrote, signed in through `/auth/dev-login`.
- `NcaafPickEm.Api.Tests` boots the real app with `WebApplicationFactory<Program>`. `SqlTestDatabase`
  creates a throwaway database `NcaafPickEm_Test_<guid>`, migrates it, and drops it at the end of
  the run — one database per run, not per test, shared through the `ApiTestFixture` collection
  fixture. The server comes from the `TEST_SQL_CONNECTION` environment variable and defaults to
  LocalDB:

  ```bash
  TEST_SQL_CONNECTION="Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True"
  ```

  API tests run with `Jobs__Enabled=false` and `Providers__*=Fixture`.

No test ever talks to Tailscale or a real tailnet. `TestAuthHandler` registers a `TestAuth` scheme
that signs a request in as whatever user id the `X-Test-User` header names — use
`ApiFactory.CreateClientAs(userId)` (or `CreateMutatingClientAs`, which adds the CSRF header) after
seeding rows with `TestUsers`; this is the default scheme for every test except the ones below. The
real header-identity handler is covered by `TailscaleAuthTests`, which drives it with real
`Tailscale-User-Login` / `Tailscale-User-Name` headers over `ApiTestFixture.CookieFactory` (no
`TestAuth` override), the same policy-scheme selection the app uses in Development/Testing and
Production alike.

Every league-scoped endpoint group must have an authorization-matrix test. The harness is one call:

```csharp
await AuthMatrix.RunAsync(fixture, HttpMethod.Get, "/api/leagues/{leagueId}/x", "/api/leagues/{leagueId}/x/settings");
```

It seeds its own league, commissioner, member, and stranger, and asserts anonymous -> 401,
non-member -> 404, member on a commissioner route -> 403, commissioner -> 2xx.
`RouteInventoryTests`/`GeneratedAuthMatrixTests` (P8-01) additionally read the booted app's
`EndpointDataSource` and assert every route is scoped, CSRF-covered, and not accidentally
anonymous, so a new endpoint mapped without a policy fails the build rather than a review.

Run a single project or a single test:

```bash
dotnet test tests/NcaafPickEm.Domain.Tests
dotnet test --filter "FullyQualifiedName~HealthEndpointTests"
```

### The opt-in live CFBD test

`CfbdLiveTests` makes real calls to the CollegeFootballData API and is excluded from a normal
`dotnet test` run (`Category=Live`). Run it only with a real key, and expect it to consume a
request against the monthly counter:

```bash
CFBD_LIVE=1 Cfbd__ApiKey=<your key> dotnet test --filter "Category=Live"
```

### Building against the fake API client (for screenshots)

Every Blazor page is built against a real, typed HTTP client (`ILeaguesApi`, `IPicksApi`, etc.)
with a Development-only fake registered under a **build symbol**, never a runtime flag — `DEBUG`
alone is not enough:

```bash
dotnet build -p:DefineConstants='DEBUG;TRACE;USE_FAKE_API'
```

Passing a raw `-p:DefineConstants=USE_FAKE_API` **replaces** the project's default constants and
silently drops `DEBUG` too (D-123) — always include `DEBUG;TRACE;` alongside it. For a one-off
Release publish (screenshotting against a hosted, real Api with fake data), publish the Web
project first with `-p:DefineConstants=USE_FAKE_API_SCREENSHOT_TEMP`, then publish the Api with
`-p:BuildProjectReferences=false` so it reuses that exact Web build instead of silently rebuilding
it plain-Release — `MapStaticAssets()`'s manifest pins each file's length at Api-publish time, so
copying files in afterward serves them truncated. Revert the temporary constant before committing;
see `Implementation/AGENT-NOTES.md` for the full recipe.

## Formatting

`.editorconfig` is law (see `Implementation/05-Conventions.md`). Before committing:

```bash
dotnet format
dotnet format --verify-no-changes   # what CI / review checks
```

The build runs code-style analyzers (`EnforceCodeStyleInBuild`) with
`TreatWarningsAsErrors`, so an unused `using` fails the build rather than lingering.

## Database and EF Core migrations

The schema is `src/NcaafPickEm.Infrastructure/Data/AppDbContext.cs` plus one file per table in
`Data/Configurations/`. Entities live in `NcaafPickEm.Domain` and carry no EF attributes; all
mapping is Fluent. The whole schema from `Implementation/02-Data-Model.md` exists as of the
`Phase0_02_InitialSchema` migration.

Install the tool once (it must be at least as new as the EF Core packages in
`Directory.Packages.props`):

```bash
dotnet tool install --global dotnet-ef
dotnet tool update  --global dotnet-ef
```

Add a migration (append-only, named `Phase<N>_<Task>_<What>`; never edit one another task
committed):

```bash
dotnet ef migrations add Phase<N>_<Task>_<What> \
  --project src/NcaafPickEm.Infrastructure \
  --startup-project src/NcaafPickEm.Api \
  --output-dir Data/Migrations
```

Apply migrations by hand:

```bash
# bash
ConnectionStrings__Default="Server=(localdb)\\MSSQLLocalDB;Database=NcaafPickEm;Trusted_Connection=True;TrustServerCertificate=True" \
  dotnet ef database update --project src/NcaafPickEm.Infrastructure --startup-project src/NcaafPickEm.Api
```

```powershell
# Windows PowerShell
$env:ConnectionStrings__Default = "Server=(localdb)\MSSQLLocalDB;Database=NcaafPickEm;Trusted_Connection=True;TrustServerCertificate=True"
dotnet ef database update --project src/NcaafPickEm.Infrastructure --startup-project src/NcaafPickEm.Api
```

`NcaafPickEm.Infrastructure` contains an `IDesignTimeDbContextFactory<AppDbContext>`, so the tools
never boot the API host. It reads `ConnectionStrings__Default` and falls back to LocalDB, which is
why `dotnet ef migrations add` works on a clean clone with no configuration at all.

**Migrating on startup** (D-015): the app applies pending migrations at startup when
`Database__MigrateOnStartup` is true. The default is **on in Development and off everywhere else**,
so a developer never has to remember `database update` and a production deploy never migrates
itself by surprise. Set the key explicitly to override either way. Tests set it to false and
migrate their own throwaway database instead.

Generated migration files are exempt from the code-style analyzers through
`src/NcaafPickEm.Infrastructure/Data/Migrations/.editorconfig`. Do not hand-write code in that
folder.

## Publish

```bash
dotnet publish src/NcaafPickEm.Api -c Release -o <output>
```

One self-contained-by-framework folder holds the API and the Blazor app. `MapStaticAssets()` serves
the Web project's `wwwroot` and its `_framework` payload with fingerprinting and Brotli/gzip
negotiation. Do **not** add `UseBlazorFrameworkFiles()`: its private static-file branch bypasses the
endpoint middleware and returns 500 for every `/_framework` request once `MapStaticAssets()` owns
those routes.

## 5. Deploy

The home server is a headless Linux box running Docker, and the app runs entirely in containers
(P8-05): one `api` container holding the API, the hosted Blazor client and the job scheduler, one
`mssql` container, and a `watchtower` container that pulls a new image when one is published.
GitHub Actions builds the image into GHCR on every merge to `main`, so a merge *is* the
deployment. Nothing is exposed to the Internet — `tailscale serve` terminates TLS on the host
with the tailnet certificate and forwards to a loopback-only port.

```
Phone (PWA) --HTTPS--> tailscale serve (host, :443) --HTTP--> 127.0.0.1:5000 -> ncaaf-api :8080
                                                                                     |
                                                                              ncaaf-db :1433
```

Everything the server needs is in [`deploy/docker/`](deploy/docker): `compose.yaml`,
`.env.example`, `backup.sh`, `restore-verify.sh`. The image is built from the repo-root
[`Dockerfile`](Dockerfile). The operator-facing version of this section, with every manual
verification listed in order, is
[`Implementation/reviews/operator-checklist.md`](Implementation/reviews/operator-checklist.md).

### First-time setup

**Prerequisites on the server**: Docker Engine with the Compose plugin
(`curl -fsSL https://get.docker.com | sh`, then `docker compose version` must print v2.x) and
Tailscale (<https://tailscale.com/download>), joined to the family's tailnet. `tailscale status`
shows the machine's tailnet hostname, e.g. `pickem.tailnet-1234.ts.net`. No .NET SDK, no SQL
Server install, nothing else.

1. **Create the state directories.** Everything the containers persist lives under
   `/srv/docker/configs/ncaaf-pickem`. The two uids are not interchangeable: the mssql image runs
   as 10001, and our own image runs as the `app` user, uid 1654.

   ```bash
   sudo mkdir -p /srv/docker/configs/ncaaf-pickem/{mssql,keys,logs}
   sudo chown -R 10001:0    /srv/docker/configs/ncaaf-pickem/mssql
   sudo chown -R 1654:1654  /srv/docker/configs/ncaaf-pickem/keys /srv/docker/configs/ncaaf-pickem/logs
   ```

   Skipping the `mssql` chown makes SQL Server exit immediately with a permission error. The
   `keys` volume no longer protects anyone's real session — Tailscale header identity is
   per-request and has no cookie — but skipping it still breaks the Development-only dev-login
   cookie across a restart, so keep the chown.

2. **Copy the compose file and the environment file** to a working directory of your choice, e.g.
   `/srv/docker/ncaaf-pickem/`:

   ```bash
   sudo mkdir -p /srv/docker/ncaaf-pickem && cd /srv/docker/ncaaf-pickem
   # from a checkout, or curl the raw files from GitHub
   cp /path/to/repo/deploy/docker/{compose.yaml,.env.example,backup.sh,restore-verify.sh} .
   cp .env.example .env && chmod 600 .env && chmod +x backup.sh restore-verify.sh
   ```

3. **Generate the VAPID pair** and put it in `.env`. Generate it once and never change it —
   replacing the pair invalidates every stored push subscription:

   ```bash
   docker run --rm ghcr.io/michael-dunn/ncaaf-pickem:latest generate-vapid
   ```

   It prints two `Push__Vapid…=` lines; copy the values into `VAPID_PUBLIC` / `VAPID_PRIVATE` and
   set `VAPID_SUBJECT` to a real `mailto:` address. (`generate-vapid` is a hidden first argument
   the app handles before it builds the host, so it works as a container argument and never
   touches configuration, the database, or the network.)

4. **Fill in the rest of `.env`**: `SA_PASSWORD` (strong; avoid `$`, which Compose reads as a
   variable reference), `PUBLIC_ORIGIN=https://<host>.<tailnet>.ts.net` with no trailing slash,
   `CFBD_API_KEY`, and `TZ`. `GHCR_USER`/`GHCR_PAT` are only needed while the image package is
   private — see "Updating" below. Every key is commented in `deploy/docker/.env.example`.

5. **Start the stack**:

   ```bash
   docker compose up -d
   docker compose ps          # db and api should both reach (healthy)
   docker compose logs -f api
   ```

   The api container waits for SQL Server (up to `Database__StartupTimeoutSeconds`, default 120),
   applies migrations itself, and only then reports ready. Watch for these three lines:

   ```
   Applying 3 pending migration(s): [...]
   Database schema is now current
   Job scheduler started; ticking every 60s with a 60-minute catch-up window
   ...
   Job scheduler heartbeat for occurrence 2026-…-04:00
   ```

   The `-04:00` on the heartbeat is the point of the Debian-based runtime image: the scheduler
   resolves `America/New_York` by name, so the container needs `tzdata` and ICU.

6. **Publish it over Tailscale**:

   ```bash
   tailscale serve --bg --https=443 http://127.0.0.1:5000
   tailscale serve status
   ```

   That is the whole TLS story — the tailnet certificate is issued and renewed by Tailscale, with
   no cron job and no `.pfx` anywhere, and it is trusted only by devices on the same tailnet.
   `App__BehindProxy=true` in the compose file is what makes the app read the `X-Forwarded-*`
   headers Serve adds, so the rate limiter sees each member's own address rather than the Docker
   gateway, and request-built invite links come out as `https://<host>…`. Serve is also what
   injects the `Tailscale-User-Login`/`Tailscale-User-Name` headers the app signs people in from
   (Phase 9) — there is no separate identity-provider step here.

7. **First-run checks**, in order:

   ```bash
   curl -fsS http://127.0.0.1:5000/health        # {"status":"ok"}
   curl -fsS http://127.0.0.1:5000/health/ready  # {"status":"ok"} — 503 while migrating
   docker compose logs api | grep -E "migration|heartbeat"
   ```

   Then, from a phone joined to the tailnet: open `https://<host>.<tailnet>.ts.net/` in Safari —
   you land signed in as your Tailscale account, no login screen — and Add to Home Screen.

8. **Schedule the nightly backup** (see "Backups" below) and run `./restore-verify.sh` once by
   hand before calling the setup done.

**Manual pending (operator), recorded in `STATUS.md`'s P8-05 row**: the app reachable from a
phone on the tailnet over HTTPS, opening signed in with no login prompt, the stack
surviving a host reboot, and the first backup file appearing the next morning — none of these can
be verified by an agent (no home server, no phone, no tailnet access in this environment). See
`Implementation/reviews/operator-checklist.md`.

### Updating

The image is published to `ghcr.io/michael-dunn/ncaaf-pickem` by
[`.github/workflows/ghcr-build.yml`](.github/workflows/ghcr-build.yml) on every push to `main`
and every `v*` tag, tagged `latest`, `sha-<short>`, and semver for tags.

- **Automatic**: the `watchtower` container polls the `latest` tag every 30 minutes and restarts
  `ncaaf-api` in place when the digest changes. It is scoped to that one container by name, so it
  never restarts the database and never updates itself.
- **By hand**: `docker compose pull && docker compose up -d`. Use this when you want to choose the
  moment.
- **Roll back**: pin the image to a known-good digest or `sha-<short>` tag in `compose.yaml` and
  `docker compose up -d`.

**Package visibility.** The repository is public, so the container package can be public too —
GitHub → Packages → `ncaaf-pickem` → Package settings → Change visibility. Then watchtower pulls
anonymously and `GHCR_USER`/`GHCR_PAT` can be left empty. If you keep the package private, the PAT
is a classic token with `read:packages` and nothing else; it is only ever needed on the pull side
(pushing uses the workflow's own `GITHUB_TOKEN`).

**Saturday rule.** Do not deploy between the first kickoff and the last final on a Saturday
(Feature 10). Watchtower has no calendar, so before a game day either stop it
(`docker compose stop watchtower`) or simply do not merge to `main`; the surest version is to
leave `main` alone from Saturday 10:00 ET to Sunday 03:00 ET.

### Backups

`deploy/docker/backup.sh` runs from the host's cron against the db container:

```
45 3 * * * /srv/docker/ncaaf-pickem/backup.sh >> /var/log/ncaaf-backup.log 2>&1
```

- It runs `BACKUP DATABASE [NcaafPickEm] … WITH CHECKSUM, INIT` inside the container, writing to
  `/var/opt/mssql/backups`, which is part of the mssql bind mount — so the host sees the files at
  `/srv/docker/configs/ncaaf-pickem/mssql/backups/NcaafPickEm_YYYYMMDD.bak`. Copy that directory
  off the box periodically: a bind mount on the same disk is not a backup.
- `.bak` files older than 30 days are pruned (inside the container, where they are owned by uid
  10001).
- The sa password is never passed in from the host — it is already in the container's own
  environment, so it never appears in the host's process list or in cron's mail.
- `deploy/docker/restore-verify.sh` restores the newest `.bak` into a throwaway
  `NcaafPickEm_RestoreCheck`, runs `DBCC CHECKDB`, prints `Users`/`Leagues`/`Picks` row counts,
  and drops it again — including when a check fails partway. Run it once after the first real
  nightly backup lands, and periodically afterwards: a green `BACKUP DATABASE` exit code is not
  proof a file is actually restorable.

### SQL access from your PC

The db container publishes `127.0.0.1:1433` on the server only. To reach it from a workstation on
the tailnet, add a TCP proxy:

```bash
tailscale serve --bg --tcp 1433 tcp://127.0.0.1:1433
```

Then connect SSMS or Azure Data Studio to `<host>.<tailnet>.ts.net,1433` as `sa` with
`SA_PASSWORD`, trusting the server certificate. Turn it off again with
`tailscale serve --tcp 1433 off` when you are done — it is a convenience, not part of the running
system.

### Local full-stack check

Before touching the server, the same image can be exercised end to end on any machine with
Docker, entirely offline (fixtures for both providers, the demo league seeded, dev-login instead
of a real tailnet):

```bash
docker compose -f deploy/docker/compose.dev.yaml up --build -d
curl -fsS http://127.0.0.1:5000/health/ready
curl -c jar "http://127.0.0.1:5000/auth/dev-login?user=michael" && curl -b jar http://127.0.0.1:5000/api/me
docker compose -f deploy/docker/compose.dev.yaml down -v
```

## 6. Operate

- **First start on an empty database**: the app fetches the season calendar and the current
  week's data from CFBD within a minute of starting — calendar, teams, this week's and next
  week's schedule, rankings and lines, about 7 provider calls, once. Watch
  `docker compose logs api` for `Reference data bootstrap`. It runs only with
  `Providers__ReferenceData=Cfbd` and jobs enabled, only when the season has no `SeasonWeeks` or
  the `Teams` table is empty, and never blocks `/health/ready`; a failure is logged and left to
  the scheduled refresh jobs. `Providers__BootstrapOnStartup=false` turns it off,
  `=true` forces it on. Until it lands, the create-league page says the calendar is on its way
  and a league created in that window gets the default weeks 1..14 (correct it afterwards in
  league settings). While no league exists at all, any signed-in user may open `/admin/data` and
  trigger a manual refresh, because nobody commissions anything yet.
- **Data status page**: `/admin/data` (any commissioner) shows the last refresh attempt/success
  per data type, the CFBD monthly call counter, the live-score source and staleness banner
  (`ScoresMayBeStale`), unmatched/needs-review games, and a recent-jobs table. Use it first when
  something looks stale.
- **Manual refresh**: `POST /api/admin/refresh/{dataType}` (audit logged) triggers an on-demand
  ingest for any data type, bypassing the SaturdayPoller's window, from the same page.
- **Unmatched games**: `GameMatcher` cannot always reconcile a provider's team-name spelling
  against the local `Teams`/`TeamAliases` tables; unmatched games surface on the data status page,
  and `POST /api/admin/unmatched/{id}/resolve` records the alias. (Known rough edge: the resolve
  picker is a raw GUID box today, not a name search — tracked as polish, not blocking.)
- **Needs review**: a schedule change discovered after a week has locked cannot be silently
  removed, so it flags the game (`GameNeedsVoidReview`) on the data status page instead — the
  commissioner decides whether to void it. A tie, or a game missing a final score, also lands
  here.
- **Corrections (override / void)**: a commissioner overrides a game's result or voids it, either
  from the week configuration page (`/leagues/{id}/config/week/{week}`, once the week is locked,
  via the "Correct" action) or with one tap from a "Needs review" row on the data status page.
  Both mutations require a 1-200 character reason, are refused before lock (409 `NotLocked`),
  write an `AuditLog` row, and trigger a real rescore synchronously through the domain event they
  raise — the leaderboard reflects the change immediately, no separate step.
- **Audit page**: `/leagues/{id}/audit` (any member) lists every correction and every
  league/role mutation, newest first, with the actor, a humanised summary, and a
  relative/absolute timestamp. Linked from the league home page ("Activity") and from each "Needs
  review" row.
- **Backups**: nightly at 03:45 local, retained 30 days. Confirm the file appears the morning
  after the first deploy, and periodically run the restore check — see "Backups" under "Deploy"
  above.
- **Saturday rules**: do not deploy from Saturday 10:00 ET to Sunday 03:00 ET — that means not
  merging to `main` (and, if you want certainty, `docker compose stop watchtower` beforehand,
  since watchtower has no calendar). During that window the `SaturdayPoller` `BackgroundService`
  re-evaluates every minute and polls live scores every 5 minutes (ESPN or Fixture) or every 10
  minutes (once ESPN has failed 3 times in a row and CFBD is the active fallback), applying each
  snapshot and scoring games as they go Final.
- **If scores look stale**: check `/admin/data` first — the staleness banner and
  `ActiveLiveScoreSource` say whether ESPN or the CFBD fallback is active and when it last
  succeeded. A manual refresh (`POST /api/admin/refresh/Scores`) always uses *today's* Eastern
  date, so it will not help catch up a date in the past or future; a provider outage recorded on
  `DataRefreshStatus` resolves itself on the next scheduled poll once the provider recovers.
- **Logs**: `ncaaf-<date>.log`, daily rolling, 31 files retained, at
  `/srv/docker/configs/ncaaf-pickem/logs/` on the host (mounted at `/app/logs`, set by
  `Serilog__LogDirectory`); `docker compose logs -f api` shows the same events on the console
  sink.
- **Container control**: `docker compose ps`, `docker compose restart api`,
  `docker compose logs -f api`, `docker compose down`. `restart: unless-stopped` brings the whole
  stack back after a host reboot, and the api container waits for SQL Server rather than
  crash-looping while it starts.
- **Backups (Docker)**: `/srv/docker/configs/ncaaf-pickem/mssql/backups/`, nightly at 03:45 from
  the host's cron via `deploy/docker/backup.sh`, 30 days retained; verify with
  `deploy/docker/restore-verify.sh`.

## 7. Where the plan lives

The requirements are the 13 stories under [`WorkItems/`](WorkItems); everything else is under
[`Implementation/`](Implementation), starting at
[`Implementation/00-README.md`](Implementation/00-README.md), which indexes every plan document
(architecture, data model, API contracts, domain algorithms, conventions, agent protocol,
traceability, phase task cards, decisions log, status board, security review, spikes,
screenshots, and `AGENT-NOTES.md`). `Implementation/07-Traceability.md` is the sign-off document:
every acceptance-criteria group in every story maps to a task and a proof (a passing test, a
screenshot, or a recorded manual verification). `Implementation/reviews/operator-checklist.md`
collects every manual, device-only verification from the whole plan into one ordered list for
whoever runs the home server.

## Package versions

Central Package Management is on: every `PackageReference` in the repo is versionless and
`Directory.Packages.props` is the single source of truth. To add a package, add a `PackageVersion`
there and a versionless `PackageReference` in the consuming project. Do not bump an existing version
without a `DECISIONS.md` entry.

Notable pins: xUnit 2.9.3, FluentAssertions **7.2.2** (8.x changed its license — see D-009),
Serilog.AspNetCore 10.0.0.

## Logging

Serilog writes to the console and to a daily rolling file at `logs/ncaaf-<date>.log` relative to the
content root (31 files retained, 64 MB roll). `logs/` is gitignored. Sinks are declared in
`src/NcaafPickEm.Api/SerilogConfiguration.cs`; levels, overrides and enrichers come from the
`Serilog` section of `appsettings*.json`. Request logging is on via `UseSerilogRequestLogging()`.

## Where things plug in

Adding a feature should be a one-line change in each of these, never a rewrite of `Program.cs`:

| What you are adding | Where the one line goes |
|---|---|
| Any Infrastructure service (DbContext, provider, job, push) | `src/NcaafPickEm.Infrastructure/DependencyInjection.cs` → `AddInfrastructure` |
| Any API service (auth, validators, filters) | `src/NcaafPickEm.Api/DependencyInjection.cs` → `AddApiServices` |
| A new endpoint group | `src/NcaafPickEm.Api/Endpoints/EndpointMapping.cs` → `MapApiEndpoints`, plus your own `Endpoints/<Feature>Endpoints.cs` |
