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

D-012  2026-09-18  P2-01  ESPN status mapping in 04 section 9 gains a `status.type.state` fallback and diacritic-folding normalization; CFBD's live scoreboard is confirmed Tier 1 ($1/mo), not Tier 2; `groups=80` is not an FBS/FCS classifier.
       Rationale: verified live in the P2-01 spike (`Implementation/spikes/providers.md`) against real ESPN payloads for 2026-09-12 (80 events, all Final), 2026-09-19 (71 events, all Scheduled, all with odds) and the rankings endpoint, plus the public CFBD tiers page and the public CFBD OpenAPI document.
       Four changes follow. (1) `04` section 9 enumerates ESPN `status.type.name` values; ESPN adds names without notice and the spelling `STATUS_END_PERIOD` is probably `STATUS_END_OF_PERIOD`. Any unrecognized name must fall back to `status.type.state` ("pre" to Scheduled, "in" to InProgress, "post" to Final only when `type.completed` is true, otherwise leave unchanged) and be logged once, rather than throwing or stalling. `state` has three stable values and is the safe backstop.
       (2) `04` section 9's normalization must fold diacritics: ESPN ships "San Jose State" with U+00E9, so lowercase-plus-strip-punctuation leaves a string that never equals CFBD's. Also match ESPN `team.location` (school name, no mascot) rather than `team.displayName`, and treat `Teams.Abbreviation` as an exact-match last resort only, because ESPN's abbreviations are its own (USA / USM / USF collide).
       (3) Feature 12 says the CFBD tiers page is self-contradictory about the real-time scoreboard tier. It is not, as of 2026-09-18: Live Scoreboard is Tier 1, $1/month, 5,000 calls; Tier 2 ($5) adds live play-by-play. Feature 12's Option 1 cost line and Option 3 fallback line should both say Tier 1 / $1. D-003 (hybrid) is unchanged and reaffirmed - ESPN is still free, keyless and richer - but the escape hatch is a dollar, not five.
       (4) Feature 12 says "FBS/FCS classification has to be inferred from the groups parameter". It cannot be. `groups=80` selects games involving an FBS team, not games where both teams are FBS; the two captures contain 185 distinct teams across 22 conference groups, including Howard, Portland State, Gardner-Webb and Cal Poly. Classification must come from CFBD `Team.classification`, which D-003 already provides. Consequence for P2-03: an unmatched ESPN event is ignored silently when either team is a known FCS school and written to `UnmatchedGames` only when both look FBS.
       Not superseded, flagged for the owning tasks rather than decided here: CFBD `Game` carries no status field (only `completed`), so P2-02 must model postponement as the game disappearing from or moving within the week payload, and P2-03's CFBD fallback is Scheduled-or-Final only; and CFBD `/games` takes `classification`, not the `division` named in the P2-01/P2-02 cards.
