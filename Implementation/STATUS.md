# Status

Live task board. The agent that owns a task updates its row. States: Todo, In Progress, In Review, Blocked, Done.

## Board

| Task | Title | Agent | Tier | State | Branch | Notes |
|---|---|---|---|---|---|---|
| P0-01 | Repo and solution scaffold | opus-p0-01 | Opus | Done | main @ 5e49ac0 | Board verified: 38 task rows, one per task in every phase file, no drift. DECISIONS D-001..D-008 present; D-009 (test stack + central package management), D-010 (.slnx), D-011 (no UseBlazorFrameworkFiles) added. `dotnet build` 0 warnings, `dotnet test` 5/5 green, `dotnet format --verify-no-changes` clean. |
| P0-02 | Database and EF Core | opus-p0-02-03 | Opus | Done | p0-02-03-database-auth | All 26 tables from `02-Data-Model.md` in one migration `Phase0_02_InitialSchema`; applied to LocalDB (27 tables incl. history, 3 filtered indexes, `EndpointHash` computed column). `SqlTestDatabase` + `ApiFactory` + `ApiTestFixture` collection fixture (one DB per run, dropped at the end). `/health/ready` does `CanConnectAsync` and 503s otherwise. D-014..D-018 logged. |
| P0-03 | Google sign-in, users, authorization plumbing | opus-p0-02-03 | Opus | Done | p0-02-03-database-auth | Cookie `ncaaf.auth` (90d sliding, HttpOnly/Secure/Lax) + Google handler; `/auth/login/google`, `/auth/callback/google` (middleware), `/auth/logout`; `/api/me` GET+PUT. `RequireLeagueMember()` / `RequireLeagueCommissioner()` + `HttpContext.GetMembership()`; CSRF filter on the whole `/api` group. Tests: `TestAuth` scheme (`X-Test-User`), `ApiFactory.CreateClientAs`, `AuthMatrix.RunAsync`, and `AuthTests` driving the real Google pipeline through a fake backchannel. D-019..D-022 logged. **Manual pending (operator): real Google login over https://localhost:7092 with a dev OAuth client** — steps are in README "Google OAuth dev setup". |
| P0-04 | Blazor PWA shell and load-time spike | | Opus | Todo | | |
| P0-05 | Season calendar domain | | Opus | Todo | | |
| P0-06 | Job scheduler infrastructure | | Opus | Todo | | |
| P1-01 | League and membership service and endpoints | | Sonnet | Todo | | Opus review |
| P1-02 | League UI | | Sonnet | Todo | | |
| P1-03 | Display names | | Sonnet | Todo | | |
| P2-05 | Fixtures and fixture providers | | Sonnet | Todo | | Start first in Phase 2 |
| P2-01 | Provider spike (real APIs) | opus-p2-01 | Opus | Done | p2-01-provider-spike, p2-01b-cfbd-captures | CFBD captures done (7 requests); only remaining gap is an in-progress ESPN payload (capture on a Saturday afternoon). Spike doc `Implementation/spikes/providers.md`; trimmed real captures (ESPN + CFBD) + verified TeamAliases draft in `tests/NcaafPickEm.Fixtures/Real/`; D-012 and D-013 logged; Feature 12 follow-ups updated. |
| P2-02 | CFBD reference data provider and ingest | | Sonnet | Todo | | Opus review |
| P2-03 | ESPN live score provider, matcher, CFBD fallback | | Opus | Todo | | |
| P2-04 | Refresh jobs, Saturday poller, data status page | | Sonnet | Todo | | Opus review on poller |
| P3-01 | Game set generator (domain) | opus-p3-01 | Opus | In review | p3-01-gameset-generator | Pure `GameSetGenerator.Generate/Preview` + input records; 23 tests. Corrected `04` section 2 (eligibility gates manual adds, lock is a result flag); D-030..D-032. No P0-05 dependency: `IsSaturdayEastern` is an input flag. |
| P3-02 | Point value resolver (domain) | | Opus | Todo | | Can start after Phase 0 |
| P3-03 | Configuration endpoints and services | | Sonnet | Todo | | Opus review |
| P3-04 | Auto-regeneration job and schedule-change handling | | Sonnet | Todo | | Opus review |
| P3-05 | Commissioner configuration UI and member week view | | Sonnet | Todo | | |
| P4-01 | Picks service and endpoints | | Opus | Todo | | |
| P4-02 | Lock job | | Opus | Todo | | |
| P4-03 | Picks UI | | Sonnet | Todo | | |
| P4-04 | Game added/removed handling for picks | | Sonnet | Todo | | |
| P5-01 | Week scorer (domain + service) | | Opus | Todo | | |
| P5-02 | Corrections: override result and void | | Sonnet | Todo | | Opus review |
| P5-03 | Standings and leaderboard queries | | Opus | Todo | | |
| P5-04 | Leaderboard UI | | Sonnet | Todo | | |
| P5-05 | Commissioner corrections UI and audit view | | Sonnet | Todo | | |
| P6-01 | Influence calculator (domain) | | Opus | Todo | | |
| P6-02 | Dashboard endpoint | | Sonnet | Todo | | Opus review |
| P6-03 | Dashboard UI | | Sonnet | Todo | | |
| P7-01 | Push subscriptions and sender | | Opus | Todo | | |
| P7-02 | Service worker, settings page, notification routing | | Sonnet | Todo | | |
| P7-03 | Reminder jobs and event notifications | | Sonnet | Todo | | Opus review |
| P8-01 | Security and correctness review | | Opus | Todo | | |
| P8-02 | Home server deployment | | Sonnet | Todo | | Orchestrator supplies credentials |
| P8-03 | End-to-end season simulation | | Opus | Todo | | |
| P8-04 | Traceability audit and docs | | Sonnet | Todo | | |

## Gates

| Phase | Passed at commit | Date | Notes |
|---|---|---|---|

## Hot-spot edits (Program.cs, DbContext, app.css, service-worker.js)

| Date | Task | File | What |
|---|---|---|---|
| 2026-09-18 | P0-01 | `src/NcaafPickEm.Api/Program.cs` | Created it. Thin composition root only: `AddInfrastructure(configuration)`, `AddApiServices()`, `MapApiEndpoints()`, Serilog, and Blazor hosting. Add services in `Infrastructure/DependencyInjection.cs` or `Api/DependencyInjection.cs` and endpoints in `Api/Endpoints/EndpointMapping.cs`, not here. |
| 2026-09-18 | P0-03 | `src/NcaafPickEm.Api/Program.cs` | Two edits: `AddApiServices(builder.Configuration)` now takes configuration (Google client id/secret), and explicit `app.UseAuthentication(); app.UseAuthorization();` after `UseStatusCodePages()`. The explicit calls are deliberate — `WebApplication` would auto-insert both *before* all user middleware, so a 401 from the authorization middleware would come back with an empty body instead of ProblemDetails. Do not delete them. |
| 2026-09-18 | P0-02 | `src/NcaafPickEm.Infrastructure/Data/AppDbContext.cs` | Created it, with every table from `02-Data-Model.md`. Adding a table = one `DbSet` here plus one file in `Data/Configurations/`; no mapping logic in the context itself. |
| 2026-09-18 | P0-03 | `src/NcaafPickEm.Api/Endpoints/EndpointMapping.cs` | Added `app.MapAuthEndpoints()` at the root, `.AddEndpointFilter<CsrfEndpointFilter>()` on the `/api` group, `api.MapMeEndpoints()`, and a Development/Testing-only `api.MapDiagnosticsEndpoints()` (P1-01 deletes that block, D-021). |
| 2026-09-18 | P0-03 | `src/NcaafPickEm.Web/Program.cs` | Added `AddAuthorizationCore()`, `AddCascadingAuthenticationState()`, and `ApiAuthenticationStateProvider` as the `AuthenticationStateProvider`. `App.razor` is untouched — the cascading state comes from DI, not a wrapper component. |
| 2026-09-18 | P0-03 | `src/NcaafPickEm.Web/_Imports.razor` | Appended four usings: `NcaafPickEm.Web.Auth`, `NcaafPickEm.Web.Components`, `Microsoft.AspNetCore.Components.Authorization`, `Microsoft.AspNetCore.Authorization`. |

## Escalations

| Date | Tasks | Question | Decision (DECISIONS id) |
|---|---|---|---|
