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

## Build strictness (from P0-01)
- `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` + `GenerateDocumentationFile` are on. An unused `using` is a build **error** (IDE0005). Private instance fields `_camelCase`; private static fields `PascalCase`.
- Central Package Management: add a `PackageVersion` to `Directory.Packages.props` and a version-less `PackageReference` in the csproj. A `Version` attribute in a csproj fails restore (NU1008). Match ASP.NET package versions to runtime 10.0.11.
- Pinned: xunit 2.9.3, FluentAssertions 7.2.2 (do not upgrade to 8), Serilog.AspNetCore 10.0.0, Mvc.Testing 10.0.11.

## Composition root (Program.cs is a hot spot; add ONE line, commit it alone)
- `builder.Services.AddInfrastructure(builder.Configuration)` -> `src/NcaafPickEm.Infrastructure/DependencyInjection.cs`. Registers `TimeProvider.System` via `TryAddSingleton` (tests substitute a fake).
- `builder.Services.AddApiServices()` -> `src/NcaafPickEm.Api/DependencyInjection.cs`.
- `app.MapApiEndpoints()` -> `src/NcaafPickEm.Api/Endpoints/EndpointMapping.cs`; creates the `/api` group. Each feature adds `api.MapXxxEndpoints()` there. Health is at root, unauthenticated.
- `public partial class Program;` exists for `WebApplicationFactory<Program>`.
- **Do NOT add `app.UseBlazorFrameworkFiles()`.** Hosting is via `MapStaticAssets()` alone (D-011); adding it 500s every `/_framework/*` request.
- **Do NOT put `#[.{fingerprint}]` placeholders back in `Web/wwwroot/index.html`** (D-013). The SDK only rewrites them when the Web project is published on its own; through the Api's publish the literal placeholder ships and the app never boots. `OverrideHtmlAssetPlaceholders` is false on purpose.
- **Do NOT set `TrimMode=full`** on the Web project (D-014): it strips component constructors reached only by reflection, so a published build renders `NotFound` and throws `CtorNotLocated`. The SDK default trim mode is what we ship.
- A publish that curls clean can still be a dead app. After any change to `index.html`, trimming, or publishing, actually render the app in a browser. Headless Edge works: `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`; `--dump-dom` fires before WASM boots, so drive it over the DevTools protocol (`--remote-debugging-port`) and poll for real content. P0-04 left a working harness recipe in `Implementation/spikes/wasm-load-time.md`.

## Client (Blazor) facts from P0-04
- The client runs with `InvariantGlobalization=true` (D-012). Local time renders fine; .NET cannot give you a zone *abbreviation* ("MDT") - use `Intl.DateTimeFormat` from JS or the UTC offset, and take Eastern strings from the server (`LockAtEasternDisplay`).
- Inject `HttpClient` (scoped, already carries the CSRF header handler and the 401 -> `/login` redirect). Register new client services in `Web/Services/DependencyInjection.cs`, not `Program.cs`.
- Shell state is `AppShellState` (cascaded by `MainLayout`): call `SetLeague(id, name, week)` / `SetTitle(...)` from a page's `OnInitialized`/`OnParametersSet`, or `ClearLeague()` outside a league. The bottom tab bar reads it to build its routes.
- Component styles go in a co-located `.razor.css`; a rule that must reach a child component's markup (a `NavLink`, say) needs `::deep`. `app.css` holds only tokens, the reset, and shell primitives.

## Fixtures
- `tests/NcaafPickEm.Fixtures` embeds `Data/**/*.json`; `FixtureLoader.Names` / `ReadText(name)` / `Read<T>(name)` with names like `"Week7_2026/schedule.json"`. `ScaffoldTests` asserts `Names` is empty; P2-05 must update it.

## Local run
- `dotnet run --project src/NcaafPickEm.Api` uses the `https` launch profile (https://localhost:7092) with `Providers__*=Fixture` preset. `appsettings.Development.json` is gitignored; the template next to it is the committed source of truth.
