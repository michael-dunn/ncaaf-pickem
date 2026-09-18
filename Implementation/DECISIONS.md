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
