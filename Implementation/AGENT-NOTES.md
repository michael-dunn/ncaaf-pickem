# Agent Notes (living file; orchestrator-maintained)

Practical facts every implementation agent needs. Read after `00-README.md`, `05-Conventions.md`, `06-Agent-Protocol.md`. Add a bullet when you hit something the next agent will hit too. Keep entries short.

## Environment
- Windows 11. .NET SDK 10.0.400 only; ASP.NET runtime 10.0.11. `dotnet-ef` 10.0.5 installed globally.
- SQL Server: only LocalDB (`(localdb)\MSSQLLocalDB`) on this machine. Tests default to it; `TEST_SQL_CONNECTION` overrides.
- No `wasm-tools` workload. Do not install workloads. IL trimming works without it; AOT would not.
- No CFBD API key is available. ESPN public scoreboard is reachable without a key. Use fixtures for all development.
- Manual device checks (iPhone install, real Google login, real push) cannot be done by agents. Do everything else, document the exact operator steps, and record the item as `Manual pending (operator)` in STATUS.md notes. Never claim a manual check passed.

## Git workflow
- `main` is the integration branch. Parallel agents work in **git worktrees** on task branches `p<phase>-<task>-<slug>`. Agents do **not** merge to `main`; report `BRANCH: <name> @ <commit>` and the orchestrator merges.
- Conventional Commits; every commit ends with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- `.gitattributes` stores LF, checks out CRLF. `.editorconfig` demands CRLF; run `dotnet format` before committing or `dotnet format --verify-no-changes` fails with ENDOFLINE.
- Migrations: append-only, named `Phase<N>_<Task>_<What>`. Never edit another task's migration.
- `src/NcaafPickEm.Infrastructure/Data/Migrations/.editorconfig` marks that folder as generated code. Without it `dotnet ef migrations add` fails the next build on IDE0161/IDE0005. Do not hand-write code there and do not reformat what EF emits.

## Build strictness (from P0-01)
- `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` + `GenerateDocumentationFile` are on. An unused `using` is a build **error** (IDE0005). Private instance fields `_camelCase`; private static fields `PascalCase`.
- Central Package Management: add a `PackageVersion` to `Directory.Packages.props` and a version-less `PackageReference` in the csproj. A `Version` attribute in a csproj fails restore (NU1008). Match ASP.NET package versions to runtime 10.0.11.
- Pinned: xunit 2.9.3, FluentAssertions 7.2.2 (do not upgrade to 8), Serilog.AspNetCore 10.0.0, Mvc.Testing 10.0.11.

## Composition root (Program.cs is a hot spot; add ONE line, commit it alone)
- `builder.Services.AddInfrastructure(builder.Configuration)` -> `src/NcaafPickEm.Infrastructure/DependencyInjection.cs`. Registers `TimeProvider.System` via `TryAddSingleton` (tests substitute a fake), `AppDbContext` against `ConnectionStrings:Default`, and `DatabaseMigratorHostedService` (D-013).
- `builder.Services.AddApiServices()` -> `src/NcaafPickEm.Api/DependencyInjection.cs`.
- `app.MapApiEndpoints()` -> `src/NcaafPickEm.Api/Endpoints/EndpointMapping.cs`; creates the `/api` group. Each feature adds `api.MapXxxEndpoints()` there. Health is at root, unauthenticated.
- `public partial class Program;` exists for `WebApplicationFactory<Program>`.
- **Do NOT add `app.UseBlazorFrameworkFiles()`.** Hosting is via `MapStaticAssets()` alone (D-011); adding it 500s every `/_framework/*` request.
- `app.UseAuthentication()` / `UseAuthorization()` are called **explicitly** in Program.cs after `UseStatusCodePages()`. Do not remove them: `WebApplication` auto-inserts both before every user middleware, and a 401 raised out there never reaches `UseStatusCodePages`, so it comes back with an empty body.

## Auth (from P0-03)
- Scope a league endpoint group with `RequireLeagueMember()` or `RequireLeagueCommissioner()` from `Api/Auth/LeagueAuthorizationExtensions.cs`, then read the membership back with `HttpContext.GetMembership()`. Never re-query the membership in a handler. Non-member is 404, member-on-commish is 403.
- The CSRF filter is on the whole `/api` group already; do not add it per endpoint. Mutations need `X-Requested-With: NcaafPickEm`.
- Signed-in identity is `ICurrentUser` (scoped). Claims are `ClaimTypes.NameIdentifier` = our `Users.Id`, `Name` = display name, `Email`.
- API tests: `[Collection(ApiTestCollection.Name)]`, take `ApiTestFixture`, seed with `TestUsers`, call `factory.CreateClientAs(userId)` (or `CreateMutatingClientAs` for the CSRF header). One authorization-matrix test per endpoint group: `AuthMatrix.RunAsync(fixture, method, memberRoute, commishRoute)` with `{leagueId}` in the templates.
- `/api/leagues/{leagueId}/ping` and `/ping/commish` are temporary probes mapped only in Development/Testing. **P1-01 deletes `DiagnosticsEndpoints.cs`, `DiagnosticsPing.cs`, and the environment block in `EndpointMapping`** and repoints `AuthMatrixTests` (D-019).

## Fixtures
- `tests/NcaafPickEm.Fixtures` embeds `Data/**/*.json`; `FixtureLoader.Names` / `ReadText(name)` / `Read<T>(name)` with names like `"Week7_2026/schedule.json"`. `ScaffoldTests` asserts `Names` is empty; P2-05 must update it.

## Local run
- `dotnet run --project src/NcaafPickEm.Api` uses the `https` launch profile (https://localhost:7092) with `Providers__*=Fixture` preset. `appsettings.Development.json` is gitignored; the template next to it is the committed source of truth.
