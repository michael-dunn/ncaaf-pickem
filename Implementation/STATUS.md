# Status

Live task board. The agent that owns a task updates its row. States: Todo, In Progress, In Review, Blocked, Done.

## Board

| Task | Title | Agent | Tier | State | Branch | Notes |
|---|---|---|---|---|---|---|
| P0-01 | Repo and solution scaffold | opus-p0-01 | Opus | Done | main @ 5e49ac0 | Board verified: 38 task rows, one per task in every phase file, no drift. DECISIONS D-001..D-008 present; D-009 (test stack + central package management), D-010 (.slnx), D-011 (no UseBlazorFrameworkFiles) added. `dotnet build` 0 warnings, `dotnet test` 5/5 green, `dotnet format --verify-no-changes` clean. |
| P0-02 | Database and EF Core | | Opus | Todo | | |
| P0-03 | Google sign-in, users, authorization plumbing | | Opus | Todo | | |
| P0-04 | Blazor PWA shell and load-time spike | opus-p0-04-05 | Opus | In Review | p0-04-05-pwa-calendar | Shell, PWA assets, HttpClient handlers, trimming/Brotli done. Spike recorded in `Implementation/spikes/wasm-load-time.md`: 2.19 MB Brotli, ~0.5 s cold / ~0.3 s warm to rendered page on localhost; PASS, D-014 confirms Blazor WASM. Screenshot `Implementation/screenshots/p0-04-shell-375.png`. **Manual pending (operator)**: iPhone home-screen install, standalone launch, and the phone/Tailscale load numbers (steps in the spike file). Fixed two blockers that made the hosted app never boot: fingerprint placeholder (D-013) and `TrimMode=full` (D-014). |
| P0-05 | Season calendar domain | | Opus | Todo | | |
| P0-06 | Job scheduler infrastructure | | Opus | Todo | | |
| P1-01 | League and membership service and endpoints | | Sonnet | Todo | | Opus review |
| P1-02 | League UI | | Sonnet | Todo | | |
| P1-03 | Display names | | Sonnet | Todo | | |
| P2-05 | Fixtures and fixture providers | | Sonnet | Todo | | Start first in Phase 2 |
| P2-01 | Provider spike (real APIs) | | Opus | Todo | | Needs CFBD key |
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

## Hot-spot edits (Program.cs, DbContext, app.css, service-worker.js)

| Date | Task | File | What |
|---|---|---|---|
| 2026-09-18 | P0-04 | `src/NcaafPickEm.Web/wwwroot/css/app.css` | Replaced the template stylesheet with the design tokens (colours, spacing, 16px base, 44px tap target, safe-area insets), the reset, the shell layout, small primitives, and the loading splash. Add feature styles in a co-located `.razor.css`, not here. |
| 2026-09-18 | P0-04 | `src/NcaafPickEm.Web/wwwroot/service-worker.js` and `service-worker.published.js` | Kept the template's asset-manifest offline caching unchanged; appended empty `push` and `notificationclick` listeners with TODOs for P7-02. |
| 2026-09-18 | P0-04 | `src/NcaafPickEm.Web/Program.cs` | One registration line: `builder.Services.AddWebServices(new Uri(builder.HostEnvironment.BaseAddress))`. Add client services in `Web/Services/DependencyInjection.cs`, not here. |
| 2026-09-18 | P0-04 | `src/NcaafPickEm.Web/_Imports.razor` | Added `@using NcaafPickEm.Web.Components` and `@using NcaafPickEm.Web.Services`. |
| 2026-09-18 | P0-04 | `src/NcaafPickEm.Web/wwwroot/index.html` | PWA head (manifest, theme colour, apple-mobile-web-app meta, `viewport-fit=cover`), branded splash, bootstrap link removed, and the stable `_framework/blazor.webassembly.js` script path (D-013). |
| 2026-09-18 | P0-04 | `src/NcaafPickEm.Api/NcaafPickEm.Api.csproj` | Comment only: why the hosted `index.html` must not use the SDK fingerprint placeholders (D-013). No behaviour change. |
| 2026-09-18 | P0-01 | `src/NcaafPickEm.Api/Program.cs` | Created it. Thin composition root only: `AddInfrastructure(configuration)`, `AddApiServices()`, `MapApiEndpoints()`, Serilog, and Blazor hosting. Add services in `Infrastructure/DependencyInjection.cs` or `Api/DependencyInjection.cs` and endpoints in `Api/Endpoints/EndpointMapping.cs`, not here. |

## Escalations

| Date | Tasks | Question | Decision (DECISIONS id) |
|---|---|---|---|
