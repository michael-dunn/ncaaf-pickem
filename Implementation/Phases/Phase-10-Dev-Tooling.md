# Phase 10 - Development tooling: movable clock and demo-week controls

Status: Implemented 2026-09-21 (P10-01 on `p10-01-dev-clock`).

Goals (from the owner):
1. Test picks, the lock, live scores and results from a browser this week, without a tailnet and
   without waiting for the fixture week (Week 7, 2026) to arrive on the real calendar.
2. Make seeding give the demo league enough to exercise a whole week, not just five members.

Depends on: Phase 9 merged (it is). This repo only.

Read first: `Infrastructure/DependencyInjection.cs` (the `TimeProvider` registration),
`Api/Simulation/SimulateCommand.cs` (the offline version of every step here),
`Infrastructure/Seeding/FixtureSeeder.cs`, `Api/Endpoints/FixtureAdminEndpoints.cs`,
`Web/Pages/Admin/DataStatusPage.razor` (the admin page pattern).

---

## Design decisions already made

- **The clock is the lever, not the data.** Every service and job already reads time through the
  one registered `TimeProvider`, and the fixture data set has games in exactly one week. Moving the
  app's clock into that week is cheaper and more honest than authoring a second fixture week that
  goes stale every seven days. D-180.
- **Only `GetUtcNow` is overridden.** Timers and timestamps stay real, so `JobScheduler` and
  `SaturdayPoller` keep ticking once a minute and simply read a shifted "now". A frozen clock still
  lets the scheduler run; it just never sees the minute change.
- **Development and Testing only.** `AddInfrastructure` registers `DevTimeProvider` there and
  `TimeProvider.System` everywhere else; the routes are mapped in the same block as
  `/auth/dev-login`. `ProductionBehaviourTests` already asserts nothing under
  `api/admin/fixture` exists in Production, which covers the new routes.
- **The demo controls go through the real services.** Generate is `GameSetService`, picks are
  `PickService` (so the lock and current-week rules apply exactly as for a member), lock is
  `LockWeekJob`, scores are `SaturdayPoller.PollOnceAsync`. Refusals come back as notes, not
  errors, because the point is to see where the week stands.
- **Client-side time is left alone.** `LockTime`'s countdown and the "last updated" labels use the
  browser clock and will look odd while the server is shifted. Fixing that means the client
  fetching "now" from the server; deferred.

## P10-01 Dev clock, demo-week controls, Dev tools page

**Scope**
- `Infrastructure/Time/DevTimeProvider.cs`: real time plus a movable offset, or one frozen
  instant; `SetNow`, `Advance`, `Freeze`, `Thaw`, `Reset`; thread-safe.
- `AddInfrastructure`: Development/Testing register `DevTimeProvider` as the `TimeProvider`
  (still `TryAdd`, so the tests' and `simulate`'s own clocks keep winning), pre-shifted by
  `Clock:NowUtc` and `Clock:Frozen`; Production keeps `TimeProvider.System`. `DemoWeekService`
  registered in the same block.
- `FixtureSeeder`: the demo league gets one default rule (Top 25) so the week's set can generate on
  its own (`EnsureCurrentWeekSetsJob`, `RegenerateGameSetsJob`, or the demo control).
- `Api/Endpoints/DevClockEndpoints.cs`: `GET`/`PUT`/`DELETE /api/admin/fixture/clock`
  (`Authenticated`; 409 when the registered clock is not the dev clock).
- `Api/Endpoints/DemoWeekEndpoints.cs`: `GET /api/admin/fixture/demo` plus `POST .../generate`,
  `.../picks?includeMe=`, `.../lock`, `.../poll?snapshot=`, `.../reset` (`Authenticated`; 404 when
  the demo league is not seeded).
- `Infrastructure/Seeding/DemoWeekService.cs`: the steps behind those routes.
- `Shared/Contracts/Dev/*`: `DevClockRequest`, `DevClockResponse`, `DemoWeekResponse`,
  `DemoWeekSetDto`, `DemoMemberDto`.
- `Web/Pages/Admin/DevToolsPage.razor` (`/admin/dev`, linked from More): clock card (jump
  presets, move-by, exact instant, freeze, reset) and demo-week card (generate, fill picks, lock,
  snapshot + poll, member table, reset behind a confirm). `IDevToolsApi`/`DevToolsApi`/
  `FakeDevToolsApi`.
- Docs: README "Moving the clock and driving a week", AGENT-NOTES Local run, D-180,
  `03-API-Contracts.md` dev-tools paragraph, `appsettings.Development.template.json` `Clock`
  section, `compose.dev.yaml` example lines.

**Done when**
- [x] `PUT /api/admin/fixture/clock {"nowUtc":"2026-10-14T16:00:00Z"}` on a Development server
      makes `GET /api/admin/fixture/demo` report week 7, and `POST .../generate` builds the set.
- [x] `POST .../picks`, `.../lock`, `.../poll?snapshot=6` walk the demo league to a scored week;
      `.../reset` clears it and puts the fixture games back to Scheduled.
- [x] `Clock__NowUtc` in the environment boots the app already shifted.
- [x] Production maps none of it (`ProductionBehaviourTests`) and reads real time.
- [x] Tests: `DevTimeProviderTests` (9), `DevClockEndpointTests` (7), `DemoWeekEndpointTests` (5).
- [x] `dotnet build -c Release` 0 warnings; `dotnet format --verify-no-changes` clean; both suites green.

## Phase exit criteria
- P10-01 merged; STATUS row Done with the merge commit; D-180 in DECISIONS.
