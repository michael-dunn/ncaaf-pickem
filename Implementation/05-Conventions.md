# 05 - Conventions

## Code

- C# 14, nullable enabled, implicit usings, `TreatWarningsAsErrors` on. `.editorconfig` from P0-01 is law; run `dotnet format` before committing.
- File-scoped namespaces. One public type per file. Records for DTOs and value objects; classes for entities.
- Domain project: no `DateTime.UtcNow`, no I/O, no EF attributes. Pure functions take inputs and return results; entity mutation happens in application services.
- Minimal API handlers: `static` methods, `TypedResults`, return `Results<Ok<T>, NotFound, ProblemHttpResult>` unions. Bind route/league membership through the shared endpoint filter; do not re-query membership in handlers.
- Validation: FluentValidation validators in `Shared`-adjacent `Api/Validation`, run by an endpoint filter. Return 400 ProblemDetails.
- Guids: `Guid.CreateVersion7()`.
- Logging: structured Serilog, message templates with named properties. Log every provider call outcome and every job run at Information; failures at Warning or Error.
- Blazor: one page per route under `Pages/<Feature>/`. Components under `Components/`. Keep JS interop in `wwwroot/js/<feature>.js` modules loaded via `IJSRuntime.InvokeAsync<IJSObjectReference>("import", ...)`. No inline `<script>`.
- CSS: a single `app.css` with CSS variables and a mobile-first 375px baseline; component `.razor.css` for local styles. Base font 16px. Tap targets 44x44 minimum. No horizontal page scroll; grids are their own `overflow-x: auto` containers.

## Folder ownership (to avoid merge conflicts between parallel agents)

| Area | Owner while a task is in progress |
|---|---|
| `src/NcaafPickEm.Domain/<Feature>/` | the domain task for that feature |
| `src/NcaafPickEm.Shared/Contracts/<Feature>/` | the backend task for that feature; frontend proposes changes via message, backend commits them |
| `src/NcaafPickEm.Api/Endpoints/<Feature>Endpoints.cs` | the backend task |
| `src/NcaafPickEm.Web/Pages/<Feature>/` | the frontend task |
| `src/NcaafPickEm.Infrastructure/Data/Migrations/` | append-only; never edit another task's migration. Rebase conflicts by regenerating your own migration. |
| `Program.cs`, `DbContext`, `app.css`, `service-worker.js` | shared hot spots. Make the smallest possible edit, commit it alone, and post in STATUS.md that you touched it. |

## Testing

- Every Domain algorithm in `04-Domain-Algorithms.md` has a dedicated test class named there. Tests are written from the story's acceptance criteria, one test per criterion where feasible, named `Given_When_Then` style: `GivenSubmittedMember_WhenGameAdded_ThenStatusRevertsToInProgress`.
- Domain tests must not touch a database or the network and must finish in under 10 seconds total.
- API tests use `WebApplicationFactory<Program>` with `Jobs__Enabled=false`, `Providers__*=Fixture`, and a real SQL Server database created per test run (`NcaafPickEm_Test_<guid>`) and dropped after. Connection string from `TEST_SQL_CONNECTION` env var; default LocalDB.
- Authorization tests are mandatory per endpoint group: anonymous -> 401, non-member -> 404, member on commish route -> 403, commish -> 2xx. A single parameterized test per group is acceptable.
- Fixtures live in `tests/NcaafPickEm.Fixtures` as JSON with a small loader. The sample week is "Week 7, 2026" with 12 Saturday FBS games, 2 FCS games (must be excluded), 1 Friday game (excluded), 1 Saturday 12:30 AM ET game that was Friday Pacific (included), 2 conference games, 4 ranked teams, spreads on 10 of 12, and a score timeline of 6 snapshots from kickoff to all-Final including one game that goes Final after midnight ET.
- `dotnet test` must pass before any task is marked Done. If a test is flaky, fix it or delete it with a DECISIONS entry; never `[Skip]` silently.

## Git

- Initialize the repo in P0-01. Default branch `main`. One branch per task: `p<phase>-<task>-<slug>` (e.g., `p3-01-gameset-generator`).
- Conventional Commits, enforced by the `git-commit` skill: `feat(picks): server-side lock enforcement`, `test(domain): influence ordering ties`, `chore(deploy): backup script`. Scope = feature folder name.
- Commit messages end with the attribution line the session requires.
- Small commits. A task branch merges to `main` only when its card's "Done when" list is complete and `dotnet test` passes. Squash is fine. Delete the branch after merge.
- Never commit secrets. `appsettings.Development.json` is gitignored; the template file is committed.

## Definition of Done (per task)

1. Every item in the card's "Done when" list is true.
2. Tests named in the card exist and pass; `dotnet build` has zero warnings.
3. Contracts, data model, or algorithm docs updated if the task changed them, in the same branch.
4. `STATUS.md` row updated to Done with the merge commit hash.
5. Any decision not already in the plan is in `DECISIONS.md`.
6. For UI tasks: a screenshot at 375px width attached to the STATUS entry (path under `Implementation/screenshots/`).

## Naming glossary

Use these words exactly, in code and docs: **League**, **Membership** (not "player"), **Commissioner**, **Member**, **Season**, **Week**, **Game** (provider record), **GameSet** / **WeekGameSet** (the league's chosen games for a week), **GameSetGame** (a game inside a set), **Pick**, **Submission** (a member's week status), **Lock** (the moment picks freeze), **PointValue**, **PointRule**, **GameSetRule**, **WeekResult**, **Standings** (season), **WeekLeaderboard**, **Influence** / **OppositePicks** (dashboard), **Void** (game excluded from scoring after lock), **Removed** (game excluded before lock).
