# Decisions

Append only. Format: `D-NNN  YYYY-MM-DD  <task id or "plan">  <one-line decision>` followed by an indented rationale. Never edit or delete an entry; supersede it with a new one that references the old ID.

D-001  2026-09-18  plan   Frontend is Blazor WebAssembly PWA hosted by the API project, pending the P0-04 load-time spike.
       Rationale: C# end to end, shared DTOs, PWA template. Fallback if the spike fails thresholds in 01-Architecture.md: Razor Pages plus vanilla JS modules for picks and dashboard.

D-002  2026-09-18  plan   Multiple commissioners per league; authorization checks Membership.Role == Commissioner, never a single owner column.
       Rationale: Feature 01 as amended by the product owner (share and demote commissioner role). Invariant: at least one active commissioner at all times.

D-003  2026-09-18  plan   Data provider is the hybrid: CFBD free tier for reference data, ESPN scoreboard for live scores, CFBD games endpoint as fallback.
       Rationale: Feature 12, decided by the product owner. Switchable by configuration.

D-004  2026-09-18  plan   Auth is ASP.NET Core cookie authentication with the Google handler and no Identity.
       Rationale: Feature 08 Option A, decided by the product owner.

D-005  2026-09-18  plan   Background jobs run in-process with Cronos cron expressions evaluated in America/New_York; no Hangfire or Quartz.
       Rationale: Feature 10 requires in-app scheduling; keeps schema and operations small. Idempotency via JobRuns(JobName, ScheduledForUtc).

D-006  2026-09-18  plan   Week results are materialized (WeekResults) and fully recomputed on every scoring event rather than incrementally updated.
       Rationale: Feature 06 requires idempotent scoring; full recompute from source rows is the simplest way to guarantee it and supports overrides and voids without special cases.

D-007  2026-09-18  plan   Season rank trend uses SeasonStandingsSnapshots written when a week becomes Complete, comparing the latest two snapshots.
       Rationale: Feature 07 trend arrows need a stable "rank after the week before" that does not shift when a later week is rescored.

D-008  2026-09-18  plan   Removed games (before lock) are soft-flagged on WeekGameSetGames rather than deleted, and picks on them are retained.
       Rationale: Feature 02 sticky manual removal, Feature 11 "game removed" notification targeting, and history in grids all need the row to exist.

D-009  2026-09-18  P0-01  Test stack is xUnit 2.9.3 + FluentAssertions pinned to 7.2.2; versions are governed centrally by Directory.Packages.props.
       Rationale: FluentAssertions 8.x moved to a paid commercial licence (Xceed) for non-OSS use, so 7.2.2 is the last freely usable line and is pinned rather than floated. xUnit stayed on v2 (the .NET 10 `dotnet new xunit` default) because v3 changes the runner model and Microsoft.AspNetCore.Mvc.Testing integration for no benefit this project needs. Central Package Management (`ManagePackageVersionsCentrally`, transitive pinning on) means parallel agents add a versionless PackageReference plus one PackageVersion line and can never disagree on a version.

D-010  2026-09-18  P0-01  Solution file is `NcaafPickEm.slnx`, not `NcaafPickEm.sln` as named in 01-Architecture.md.
       Rationale: .NET 10's `dotnet new sln` emits the XML slnx format by default and groups src/ and tests/ into solution folders automatically. Supported by the CLI, Visual Studio 2022 17.13+, and Rider 2025.1+. Supersedes the file name in 01-Architecture.md only; the layout it describes is unchanged.

D-011  2026-09-18  P0-01  The Api hosts the Blazor WASM app through `app.MapStaticAssets()` alone. `UseBlazorFrameworkFiles()` must not be added.
       Rationale: verified by Release publish and a live smoke test. In .NET 10 the host's static-asset endpoint manifest already contains the referenced WASM project's `_framework` payload with fingerprinting and Brotli/gzip negotiation. `UseBlazorFrameworkFiles()` inserts a private StaticFileMiddleware branch that bypasses the endpoint middleware, so every `/_framework/*` request 500s with "The request reached the end of the pipeline without executing the endpoint". P0-04 owns PWA and trimming tuning but must not reintroduce that call.

D-012  2026-09-18  P0-02  Enums that both entities and DTOs need live in `NcaafPickEm.Shared/Enums`, and `Domain` takes a project reference on `Shared`.
       Rationale: `MembershipRole`, `GameStatus`, `SubmissionStatus` and friends are part of the persisted schema *and* part of the wire contract. Defining them twice guarantees they drift and forces a mapping layer nobody wants; defining them in Domain would force `Shared` (and therefore the Blazor client) to reference Domain. `Shared` has no logic and no dependencies, so `Domain -> Shared` keeps the graph acyclic and Domain still has no EF, HTTP, or clock dependency. `01-Architecture.md`'s dependency-direction line is updated in the same commit. Enums that only a DTO needs (`Trend`, `MyOutcome`, `GridCell.Outcome`, `InvitePreview.State`) still belong to `Shared/Contracts` and are added by the phase that defines them.

D-013  2026-09-18  P0-02  The app applies pending EF migrations at startup when `Database__MigrateOnStartup` is true; the default is true in Development and false in every other environment.
       Rationale: a developer should never have to remember `dotnet ef database update` after pulling a migration, and a home-server deploy should never migrate itself during an unattended restart. `DatabaseMigratorHostedService` reads the flag and falls back to `IHostEnvironment.IsDevelopment()`. API tests set it to false: `SqlTestDatabase` migrates the throwaway database before the host boots, so the two must not race. P8-02's deploy script runs `database update` as an explicit step.

D-014  2026-09-18  P0-02  Entity timestamps are `DateTime` in UTC mapped to `datetime2`, with a global value converter that stamps `Kind = Utc` on read; DTOs keep `DateTimeOffset` per `03-API-Contracts.md`.
       Rationale: `02-Data-Model.md` specifies `datetime2` UTC columns, and `datetimeoffset` would store an offset the app never uses (all logic converts through `SeasonCalendar`). SQL Server returns `DateTimeKind.Unspecified`, which silently behaves as local time in arithmetic and in JSON; `UtcDateTimeConverter` removes that whole class of bug at the mapping layer instead of trusting every caller. Columns that are genuinely a calendar date (`Games.KickoffEasternDate`, `UnmatchedGames.GameDate`) use `DateOnly`/`date` and bypass the converter.

D-015  2026-09-18  P0-02  `PushSubscriptions.Endpoint` keeps its `nvarchar(2048)` column; the unique constraint the data model asks for is enforced on a persisted computed `EndpointHash binary(32)` (SHA-256 of the endpoint).
       Rationale: SQL Server caps a nonclustered index key at 1700 bytes and `nvarchar(2048)` is 4096, so a unique index on the column itself cannot be created. Truncating the column would risk mangling a real push endpoint. The computed column is maintained by the database, so upsert code just writes `Endpoint`, and P7-01 looks a device up by hash rather than by a 2 KB string comparison. `02-Data-Model.md` records the extra column.

D-016  2026-09-18  P0-02  Every foreign key in `AppDbContext` uses `DeleteBehavior.Restrict`.
       Rationale: nothing in this app hard-deletes a principal row — memberships and game-set games are soft-flagged so history survives (D-008) — and the schema has several diamond paths (`Picks -> Memberships -> Leagues` and `Picks -> WeekGameSetGames -> WeekGameSets -> Leagues`) that SQL Server rejects outright under cascade. Restrict everywhere makes an accidental delete fail loudly instead of quietly taking picks and results with it.

D-017  2026-09-18  P0-03  The Google handler is always registered; when `Google:ClientId` / `Google:ClientSecret` are absent it is configured with placeholder credentials rather than skipped.
       Rationale: `GoogleOptions` validates that both are non-empty, so skipping registration would mean `/auth/login/google` route-not-found on a fixtures-only dev box and a different pipeline shape in tests than in production. With placeholders the app boots identically everywhere and a challenge simply fails at Google, which is the honest outcome for a machine with no OAuth client. Tests never reach Google at all: `AuthTests` replaces `GoogleOptions.BackchannelHttpHandler` with `FakeGoogleBackchannel` and drives the real challenge, state, correlation cookie, code exchange, and `OnTicketReceived` path offline.

D-018  2026-09-18  P0-03  API tests authenticate through a `TestAuth` scheme that reads `X-Test-User`; the handler looks nothing up and creates nothing.
       Rationale: the card asks for a test scheme so no test needs Google. Making the handler upsert users would give tests a second, divergent way to create accounts; instead tests seed rows with `TestUsers` and pass the id, so the database stays the one place a user comes into existence. `ApiFactory` registers the scheme as the default in `ConfigureTestServices`, which runs after the app's own `AddAuthentication`, so the cookie scheme stays registered and `ApiTestFixture.CookieFactory` can still exercise it for the Google tests. A request with no header stays anonymous and gets the same 401 as production.

D-019  2026-09-18  P0-03  `/api/leagues/{leagueId}/ping` and `/ping/commish` exist as league-scoping probes, mapped only in Development and Testing. P1-01 deletes them.
       Rationale: the authorization matrix is mandatory per endpoint group (05-Conventions.md) and `AuthMatrix` had to be proven against something, but no real league-scoped route exists until P1-01. Two do-nothing GETs cost nothing, are unreachable in Production, and let the harness ship exercised rather than untested. P1-01 must delete `DiagnosticsEndpoints.cs`, `DiagnosticsPing.cs`, and the `if (IsDevelopment || IsEnvironment("Testing"))` block in `EndpointMapping`, and point `AuthMatrixTests` at the real routes.

D-020  2026-09-18  P0-03  The CSRF header filter is applied to the `/api` group only, not to `/auth`.
       Rationale: `03-API-Contracts.md` scopes the rule to `/api`, and `POST /auth/logout` is already covered by the session cookie being `SameSite=Lax` — a cross-site form post never carries it, so there is nothing for a forged logout to act on. Adding the filter to `/auth` would also break any future plain-form fallback for sign-out without buying protection. `LogoutButton` sends the header anyway, so nothing changes if a later task widens the rule.
