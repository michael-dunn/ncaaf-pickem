# Status

Live task board. The agent that owns a task updates its row. States: Todo, In Progress, In Review, Blocked, Done.

## Board

| Task | Title | Agent | Tier | State | Branch | Notes |
|---|---|---|---|---|---|---|
| P0-01 | Repo and solution scaffold | opus-p0-01 | Opus | Done | main @ 5e49ac0 | Board verified: 38 task rows, one per task in every phase file, no drift. DECISIONS D-001..D-008 present; D-009 (test stack + central package management), D-010 (.slnx), D-011 (no UseBlazorFrameworkFiles) added. `dotnet build` 0 warnings, `dotnet test` 5/5 green, `dotnet format --verify-no-changes` clean. |
| P0-02 | Database and EF Core | opus-p0-02-03 | Opus | Done | p0-02-03-database-auth | All 26 tables from `02-Data-Model.md` in one migration `Phase0_02_InitialSchema`; applied to LocalDB (27 tables incl. history, 3 filtered indexes, `EndpointHash` computed column). `SqlTestDatabase` + `ApiFactory` + `ApiTestFixture` collection fixture (one DB per run, dropped at the end). `/health/ready` does `CanConnectAsync` and 503s otherwise. D-014..D-018 logged. |
| P0-03 | Google sign-in, users, authorization plumbing | opus-p0-02-03 | Opus | Done | p0-02-03-database-auth | Cookie `ncaaf.auth` (90d sliding, HttpOnly/Secure/Lax) + Google handler; `/auth/login/google`, `/auth/callback/google` (middleware), `/auth/logout`; `/api/me` GET+PUT. `RequireLeagueMember()` / `RequireLeagueCommissioner()` + `HttpContext.GetMembership()`; CSRF filter on the whole `/api` group. Tests: `TestAuth` scheme (`X-Test-User`), `ApiFactory.CreateClientAs`, `AuthMatrix.RunAsync`, and `AuthTests` driving the real Google pipeline through a fake backchannel. D-019..D-022 logged. **Manual pending (operator): real Google login over https://localhost:7092 with a dev OAuth client** — steps are in README "Google OAuth dev setup". |
| P0-04 | Blazor PWA shell and load-time spike | opus-p0-04-05 | Opus | Done | p0-04-05-pwa-calendar | Shell, PWA assets, HttpClient handlers, trimming/Brotli done. Spike recorded in `Implementation/spikes/wasm-load-time.md`: 2.19 MB Brotli, ~0.5 s cold / ~0.3 s warm to rendered page on localhost; PASS, D-025 confirms Blazor WASM. Screenshot `Implementation/screenshots/p0-04-shell-375.png`. **Manual pending (operator)**: iPhone home-screen install, standalone launch, and the phone/Tailscale load numbers (steps in the spike file). Fixed two blockers that made the hosted app never boot: fingerprint placeholder (D-024) and `TrimMode=full` (D-025). |
| P0-05 | Season calendar domain | opus-p0-04-05 | Opus | Done | p0-04-05-pwa-calendar | `Domain/Seasons/SeasonCalendar` (+ `SeasonWeek`, `ISeasonWeekSource`, `SeasonState`, `CurrentWeek`, `LeagueWeekRange`), `FixtureSeasonWeekSource` for 2026 (weeks 0-15, code not JSON, D-026), `GET /api/seasons/{year}/weeks`. `SeasonCalendarTests` 28 tests + `SeasonEndpointTests` 3, all green. Endpoint carries `// TODO P0-03: RequireAuthorization("Authenticated")` - the policy does not exist yet. |
| P0-06 | Job scheduler infrastructure | opus-p0-06 | Opus | In Review | p0-06-job-scheduler | `Infrastructure/Jobs/`: `IScheduledJob`, `IOneShotJob` (the card's `IOneShotScheduler`, D-029), `SchedulerTick.TickAsync(nowUtc, ct)` holding all the logic, `JobScheduler : BackgroundService` ticking it every minute off `TimeProvider`, `HeartbeatJob` (`*/5 * * * *`), `AddScheduledJob<T>()` / `AddOneShotJob<T>()`. Cronos 0.13.0 evaluated in `SeasonCalendar.Eastern`; idempotency via `JobRuns(JobName, ScheduledForUtc)` with insert-before-run and duplicate-key-means-skip; 60-minute bounded catch-up (D-028). `GET /api/admin/data-status` + `RequireAnyLeagueCommissioner()` (D-030); P2-04 extends `AdminEndpoints`. 16 new tests (`JobSchedulerTests`, `OneShotTests`, `AdminDataStatusTests`); 86 green overall. Heartbeat verified against LocalDB with `Jobs__Enabled=true`. How to add a job: AGENT-NOTES "Jobs". |
| P1-01 | League and membership service and endpoints | | Sonnet | Todo | | Opus review |
| P1-02 | League UI | | Sonnet | Todo | | |
| P1-03 | Display names | | Sonnet | Todo | | |
| P2-05 | Fixtures and fixture providers | | Sonnet | Todo | | Start first in Phase 2 |
| P2-01 | Provider spike (real APIs) | opus-p2-01 | Opus | Done | p2-01-provider-spike, p2-01b-cfbd-captures | CFBD captures done (7 requests); only remaining gap is an in-progress ESPN payload (capture on a Saturday afternoon). Spike doc `Implementation/spikes/providers.md`; trimmed real captures (ESPN + CFBD) + verified TeamAliases draft in `tests/NcaafPickEm.Fixtures/Real/`; D-012 and D-013 logged; Feature 12 follow-ups updated. |
| P2-02 | CFBD reference data provider and ingest | | Sonnet | Todo | | Opus review |
| P2-03 | ESPN live score provider, matcher, CFBD fallback | | Opus | Todo | | |
| P2-04 | Refresh jobs, Saturday poller, data status page | | Sonnet | Todo | | Opus review on poller |
| P3-01 | Game set generator (domain) | | Opus | Todo | | Can start after Phase 0 |
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
| 0 | (merge commit of p0-04-05-pwa-calendar; see git log) | 2026-09-18 | P0-01..P0-05 merged, 70 tests green. P0-06 in flight (only P2-04, P3-04, P4-02 need it). Manual pending (operator): real Google login with a dev OAuth client; iPhone PWA install and load timing over Tailscale (steps in README and Implementation/spikes/wasm-load-time.md). |

## Hot-spot edits (Program.cs, DbContext, app.css, service-worker.js)

| Date | Task | File | What |
|---|---|---|---|
| 2026-09-18 | P0-05 | `src/NcaafPickEm.Api/Endpoints/EndpointMapping.cs` | One line: `api.MapSeasonEndpoints();`, and removed the `_ = api;` placeholder now that the group has a real member. |
| 2026-09-18 | P0-05 | `src/NcaafPickEm.Infrastructure/DependencyInjection.cs` | Two lines: `TryAddSingleton<SeasonCalendar>()` and `TryAddSingleton<ISeasonWeekSource, FixtureSeasonWeekSource>()`. P2-02 swaps the week source by `Providers:ReferenceData`. |
| 2026-09-18 | P0-04 | `src/NcaafPickEm.Web/wwwroot/css/app.css` | Replaced the template stylesheet with the design tokens (colours, spacing, 16px base, 44px tap target, safe-area insets), the reset, the shell layout, small primitives, and the loading splash. Add feature styles in a co-located `.razor.css`, not here. |
| 2026-09-18 | P0-04 | `src/NcaafPickEm.Web/wwwroot/service-worker.js` and `service-worker.published.js` | Kept the template's asset-manifest offline caching unchanged; appended empty `push` and `notificationclick` listeners with TODOs for P7-02. |
| 2026-09-18 | P0-04 | `src/NcaafPickEm.Web/Program.cs` | One registration line: `builder.Services.AddWebServices(new Uri(builder.HostEnvironment.BaseAddress))`. Add client services in `Web/Services/DependencyInjection.cs`, not here. |
| 2026-09-18 | P0-04 | `src/NcaafPickEm.Web/_Imports.razor` | Added `@using NcaafPickEm.Web.Components` and `@using NcaafPickEm.Web.Services`. |
| 2026-09-18 | P0-04 | `src/NcaafPickEm.Web/wwwroot/index.html` | PWA head (manifest, theme colour, apple-mobile-web-app meta, `viewport-fit=cover`), branded splash, bootstrap link removed, and the stable `_framework/blazor.webassembly.js` script path (D-024). |
| 2026-09-18 | P0-04 | `src/NcaafPickEm.Api/NcaafPickEm.Api.csproj` | Comment only: why the hosted `index.html` must not use the SDK fingerprint placeholders (D-024). No behaviour change. |
| 2026-09-18 | P0-01 | `src/NcaafPickEm.Api/Program.cs` | Created it. Thin composition root only: `AddInfrastructure(configuration)`, `AddApiServices()`, `MapApiEndpoints()`, Serilog, and Blazor hosting. Add services in `Infrastructure/DependencyInjection.cs` or `Api/DependencyInjection.cs` and endpoints in `Api/Endpoints/EndpointMapping.cs`, not here. |
| 2026-09-18 | P0-03 | `src/NcaafPickEm.Api/Program.cs` | Two edits: `AddApiServices(builder.Configuration)` now takes configuration (Google client id/secret), and explicit `app.UseAuthentication(); app.UseAuthorization();` after `UseStatusCodePages()`. The explicit calls are deliberate — `WebApplication` would auto-insert both *before* all user middleware, so a 401 from the authorization middleware would come back with an empty body instead of ProblemDetails. Do not delete them. |
| 2026-09-18 | P0-02 | `src/NcaafPickEm.Infrastructure/Data/AppDbContext.cs` | Created it, with every table from `02-Data-Model.md`. Adding a table = one `DbSet` here plus one file in `Data/Configurations/`; no mapping logic in the context itself. |
| 2026-09-18 | P0-03 | `src/NcaafPickEm.Api/Endpoints/EndpointMapping.cs` | Added `app.MapAuthEndpoints()` at the root, `.AddEndpointFilter<CsrfEndpointFilter>()` on the `/api` group, `api.MapMeEndpoints()`, and a Development/Testing-only `api.MapDiagnosticsEndpoints()` (P1-01 deletes that block, D-021). |
| 2026-09-18 | P0-03 | `src/NcaafPickEm.Web/Program.cs` | Added `AddAuthorizationCore()`, `AddCascadingAuthenticationState()`, and `ApiAuthenticationStateProvider` as the `AuthenticationStateProvider`. `App.razor` is untouched — the cascading state comes from DI, not a wrapper component. |
| 2026-09-18 | P0-03 | `src/NcaafPickEm.Web/_Imports.razor` | Appended four usings: `NcaafPickEm.Web.Auth`, `NcaafPickEm.Web.Components`, `Microsoft.AspNetCore.Components.Authorization`, `Microsoft.AspNetCore.Authorization`. |
| 2026-09-18 | P0-06 | `src/NcaafPickEm.Infrastructure/DependencyInjection.cs` | One line, `services.AddJobScheduler(configuration)`, after the season-calendar registrations so the migrator is registered before the scheduler and the schema exists by the first tick. Later phases add their jobs with `AddScheduledJob<T>()` / `AddOneShotJob<T>()` on the next line, not in `Program.cs`. `Program.cs` itself is untouched. |
| 2026-09-18 | P0-06 | `src/NcaafPickEm.Api/Endpoints/EndpointMapping.cs` | One line, `api.MapAdminEndpoints();`, and removed the now-redundant `//   api.MapAdminEndpoints(); (P0-06)` placeholder comment. |
| 2026-09-18 | P0-06 | `Directory.Packages.props` | Added a "Background jobs" ItemGroup with `Cronos` 0.13.0 (latest stable), referenced version-less from `NcaafPickEm.Infrastructure`. |

## Escalations

| Date | Tasks | Question | Decision (DECISIONS id) |
|---|---|---|---|
