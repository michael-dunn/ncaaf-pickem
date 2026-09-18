# Phase 0 - Foundation

Stories: 08 Authentication, 10 Hosting and Platform, 13 Season Calendar.
Goal: a running, deployable skeleton that every later phase plugs into. One Opus agent, tasks mostly sequential. Nothing else starts until the exit criteria pass.

Read first: `00-README.md`, `01-Architecture.md`, `02-Data-Model.md`, `05-Conventions.md`, `06-Agent-Protocol.md`, WorkItems 08, 10, 13.

---

## P0-01 Repo and solution scaffold
Tier: Opus. Depends on: nothing.

Deliverables
- `git init`, `main` branch, `.gitignore` (dotnet, VS, Rider, `appsettings.Development.json`), `.editorconfig`, `Directory.Build.props` (nullable, warnings as errors, LangVersion latest), `global.json` pinning .NET 10 SDK.
- Solution and projects per `01-Architecture.md` layout, with project references wired and building empty.
- `README.md` at repo root: how to run locally with fixtures, how to run tests.
- Serilog configured in `Api` (console + rolling file under `logs/`).
- `/health` and `/health/ready` endpoints.
- Verify `Implementation/STATUS.md` lists every task from every phase file and `Implementation/DECISIONS.md` has D-001 to D-008; fix any drift.

Done when
- `dotnet build` and `dotnet test` succeed on a clean clone (one placeholder test per test project).
- First commit on `main` uses Conventional Commits.

## P0-02 Database and EF Core
Tier: Opus. Depends on: P0-01.

Deliverables
- `AppDbContext` with every table in `02-Data-Model.md` (all phases; the schema is small enough to create once). Configurations in `Infrastructure/Data/Configurations/<Entity>Configuration.cs`.
- Initial migration `Phase0_02_InitialSchema`. Indexes from the data model's index list.
- Connection string from `ConnectionStrings__Default`. `dotnet ef` tooling documented in README.
- Test helper `SqlTestDatabase` that creates `NcaafPickEm_Test_<guid>`, migrates, and drops on dispose. Reads `TEST_SQL_CONNECTION`, defaults to LocalDB.

Done when
- Migration applies to a fresh SQL Server database and to LocalDB.
- One API test proves `WebApplicationFactory` + `SqlTestDatabase` works end to end (`/health/ready` returns 200).

## P0-03 Google sign-in, users, authorization plumbing
Tier: Opus. Depends on: P0-02. Story 08.

Deliverables
- Cookie auth + Google handler per Feature 08 Option A. Cookie: 90-day sliding expiration, `HttpOnly`, `Secure`, `SameSite=Lax`, name `ncaaf.auth`. `/auth/login/google`, `/auth/callback/google`, `/auth/logout` per `03-API-Contracts.md`.
- On callback: upsert `Users` by `GoogleSubject`; set `Email`, initial `DisplayName` (trimmed to 30), `LastLoginUtc`. Sign in with our own claims principal (UserId, DisplayName).
- `/api/me` GET and PUT.
- Authorization policies `Authenticated`, `LeagueMember`, `LeagueCommissioner`, and the `LeagueMembershipEndpointFilter` that resolves `{leagueId}` to the caller's active membership, returning 404 when absent and 403 when a Commish route is hit by a Member.
- CSRF endpoint filter requiring `X-Requested-With: NcaafPickEm` on mutations.
- Test authentication handler for API tests (`TestAuth` scheme that reads `X-Test-User` header) so no test needs Google.
- Blazor: `AuthStateProvider` that calls `/api/me`, a `LoginPage` with a "Sign in with Google" button (full-page redirect), a `LogoutButton`.

Done when
- `AuthTests`: first login creates user, second login matches by subject, logout clears cookie, `/api/me` 401 when anonymous.
- Auth matrix test harness exists and is used by a sample protected route.
- Manual: real Google login works locally over `https://localhost` with a dev OAuth client (document the steps in README).

## P0-04 Blazor PWA shell and load-time spike
Tier: Opus. Depends on: P0-01. Story 10.

Deliverables
- Blazor WASM project hosted by `Api`, PWA-enabled: `manifest.webmanifest` (name, short_name, `display: standalone`, theme color, icons 192/512/apple-touch), `service-worker.js` with offline shell caching and an empty `push` handler stub, splash/loading screen.
- App layout: top bar with league name, bottom tab bar (Picks, Dashboard, Leaderboard, More) sized for thumbs, safe-area insets, 375px baseline, `app.css` tokens (colors, spacing, 16px base).
- Global `HttpClient` with the CSRF header handler and 401 -> login redirect handler.
- Trimming and Brotli precompression enabled for Release.
- **Spike**: publish Release, run on the home server or a laptop over Tailscale, load on an iPhone in Safari and as a home-screen app. Record first-load and repeat-load times in `Implementation/spikes/wasm-load-time.md`.

Done when
- App installs to iPhone home screen and opens standalone.
- Spike numbers recorded. If first load > 5 s or repeat load > 2 s, stop and escalate; the orchestrator decides on the Razor Pages fallback and logs D-00x before Phase 1 UI starts.

## P0-05 Season calendar domain
Tier: Opus. Depends on: P0-01. Story 13.

Deliverables
- `Domain/Seasons/SeasonCalendar` implementing `04-Domain-Algorithms.md` section 1, using `TimeProvider`.
- `SeasonWeeks` loader interface (`ISeasonWeekSource`) with a fixture implementation for 2026 (weeks 0 to 15, `IsRegularSeason` false from 15).
- `/api/seasons/{year}/weeks` endpoint.

Done when
- `SeasonCalendarTests`: Friday-Pacific-is-Saturday-Eastern, week window boundaries across the November DST change, current week before/inside/after season, default league range excludes Week 0 and championship week.

## P0-06 Job scheduler infrastructure
Tier: Opus. Depends on: P0-02.

Deliverables
- `IScheduledJob { string Name; string CronExpression (Eastern); Task RunAsync(DateTimeOffset scheduledFor, CancellationToken) }`.
- `JobScheduler : BackgroundService` using Cronos; evaluates cron in Eastern; records `JobRuns` with `UQ(JobName, ScheduledForUtc)` so a restart does not double-run; disabled when `Jobs__Enabled=false`.
- `IOneShotScheduler` for per-week one-shots (used by lock and the Saturday reminder), persisted by reading target times from the DB each minute rather than in-memory timers.
- A `HeartbeatJob` every 5 minutes as proof of life.
- `DataRefreshStatus` and `JobRuns` exposed on a minimal `/api/admin/data-status` (fields other phases fill later).

Done when
- `JobSchedulerTests`: a job scheduled for a past minute runs once; running the scheduler twice for the same minute does not double-insert; disabled flag runs nothing.

---

## Phase exit criteria
- Clean clone builds and tests green.
- Google login works locally; test auth handler works in tests.
- PWA installs on an iPhone; load-time spike recorded and decision logged.
- `SeasonCalendar` tests green.
- Scheduler runs heartbeat against SQL Server.
- `STATUS.md` has every task row; `DECISIONS.md` has D-001 to D-008.
