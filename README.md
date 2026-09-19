# NCAAF Pick Em

A mobile-first PWA for a family college-football pick-em league. Commissioners configure rules that
select Saturday FBS games each week and assign point values; members tap winners before the first
Saturday kickoff; at lock an influence dashboard shows which games matter most.

- Requirements: [`WorkItems/`](WorkItems) (13 feature stories — these are the spec).
- Implementation plan: [`Implementation/`](Implementation) — start at
  [`Implementation/00-README.md`](Implementation/00-README.md).
- Live task board: [`Implementation/STATUS.md`](Implementation/STATUS.md).

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.400 | Pinned in `global.json` (`rollForward: latestFeature`). |
| SQL Server | LocalDB or a full instance | LocalDB (`(localdb)\MSSQLLocalDB`) is enough for development and tests. |
| `dotnet-ef` | 10.0.x | `dotnet tool install --global dotnet-ef` — needed from P0-02 onwards. |

Optional: the `wasm-tools` workload. It is **not** required to build, run, or publish the Blazor
WebAssembly app; it is only needed if we ever turn on AOT (`RunAOTCompilation`). Trimming and
Brotli precompression work without it.

## Solution layout

```
NcaafPickEm.slnx
src/
  NcaafPickEm.Domain/          Entities, value objects, pure domain services.  -> Shared (enums only)
  NcaafPickEm.Shared/          DTOs and enums shared by Api and Web. No logic.
  NcaafPickEm.Infrastructure/  EF Core, migrations, providers, push, jobs.  -> Domain, Shared
  NcaafPickEm.Api/             Composition root, minimal endpoints, auth, hosts the Web app.
                                                                            -> Infrastructure, Shared, Web
  NcaafPickEm.Web/             Blazor WASM PWA.                             -> Shared
tests/
  NcaafPickEm.Domain.Tests/    Pure unit tests. No DB, no network, under 10 s.
  NcaafPickEm.Api.Tests/       WebApplicationFactory<Program> + real SQL Server.
  NcaafPickEm.Fixtures/        Embedded JSON fixtures plus `FixtureLoader`.
deploy/                        Deployment scripts and the production config template (P8-02).
Implementation/  WorkItems/    Plan and requirements.
```

Dependency direction is enforced by project references: `Web -> Shared`;
`Api -> Infrastructure -> Domain`; `Api -> Shared`; `Infrastructure -> Shared`;
`Domain -> Shared` (enums only — see D-014). `Shared` references nothing.

## Run locally with fixtures

```bash
dotnet run --project src/NcaafPickEm.Api
```

The `https` launch profile (the default) listens on `https://localhost:7092` and
`http://localhost:5204`, and already sets `Providers__ReferenceData=Fixture` and
`Providers__LiveScores=Fixture` so nothing calls a live API. To be explicit, or when running
without the profile:

```bash
Providers__ReferenceData=Fixture Providers__LiveScores=Fixture \
  dotnet run --project src/NcaafPickEm.Api --launch-profile https
```

On Windows PowerShell:

```powershell
$env:Providers__ReferenceData = "Fixture"; $env:Providers__LiveScores = "Fixture"
dotnet run --project src/NcaafPickEm.Api --launch-profile https
```

Probes: `GET /health` (liveness) and `GET /health/ready` (readiness; P0-02 adds the database
round-trip). Both return `{"status":"ok"}`.

The fixture data set (the "Week 7, 2026" sample week: 16 games, rankings, lines, and six
live-score snapshots from kickoff to all-Final) lives under
`tests/NcaafPickEm.Fixtures/Data/Week7_2026/`, loaded automatically at startup into an empty
database whenever `Providers:ReferenceData` is `Fixture`.

To get a signed-in demo league without a Google account, add:

```bash
Providers__ReferenceData=Fixture Providers__LiveScores=Fixture Seed__DemoLeague=true \
  dotnet run --project src/NcaafPickEm.Api --launch-profile https
```

```powershell
$env:Providers__ReferenceData = "Fixture"; $env:Providers__LiveScores = "Fixture"
$env:Seed__DemoLeague = "true"
dotnet run --project src/NcaafPickEm.Api --launch-profile https
```

This seeds the "Family League" demo league (members Michael, Alyson, Dance, Alex, Daniel; Michael
is Commissioner). Then visit <https://localhost:7092/auth/dev-login?user=michael> (or `alyson`,
`dance`, `alex`, `daniel`) to sign in as that member — no OAuth client needed. Seeding and
dev-login are both Development/Testing only and never run in Production.

Step through the live-score timeline (kickoff through all-Final, snapshots 1-6) with:

```bash
curl -X POST https://localhost:7092/api/admin/fixture/snapshot/3 --cookie-jar cookies.txt --cookie cookies.txt
curl https://localhost:7092/api/admin/fixture/snapshot --cookie cookies.txt
```

(sign in through `/auth/dev-login` first so the cookie jar has a session; these two routes
require `Authenticated` like the rest of `/api`).

### Local configuration

`src/NcaafPickEm.Api/appsettings.Development.json` is **gitignored**. Copy the committed template
and fill it in:

```bash
cp src/NcaafPickEm.Api/appsettings.Development.template.json \
   src/NcaafPickEm.Api/appsettings.Development.json
```

Every key can also be supplied as an environment variable with `__` as the separator. The full key
list lives in `Implementation/01-Architecture.md`:

```
ConnectionStrings__Default
Google__ClientId, Google__ClientSecret
Cfbd__ApiKey
Providers__LiveScores = Espn | Cfbd | Fixture
Providers__ReferenceData = Cfbd | Fixture
Push__VapidPublicKey, Push__VapidPrivateKey, Push__Subject
App__PublicOrigin = https://<tailnet-host>
Jobs__Enabled = true | false
```

No secrets go in the repo. `deploy/appsettings.Production.template.json` documents every key with a
placeholder for the home server.

### Running against real CFBD data

Reference data (teams, conferences, schedule, rankings, lines) can come from the live
CollegeFootballData API instead of the fixture set. Set `Providers__ReferenceData=Cfbd` and
`Cfbd__ApiKey=<your key>` (get a free-tier key at <https://collegefootballdata.com/key>);
`Providers__LiveScores` is independent and can stay `Fixture` or move to `Espn` on its own. Nothing
calls CFBD automatically — P2-04's jobs (and, until then, a manual call to
`ReferenceDataIngestService`) are what actually fetch and upsert data; see
`Implementation/AGENT-NOTES.md` ("Reference data ingest") for the service names and how ingest
failures are recorded on `GET /api/admin/data-status`.

### Web push (VAPID) keys

Web push needs a VAPID key pair. The app **boots fine without one** — `GET /api/push/vapid-public-key`
answers 503 and notifications are logged as Failed — so only set these when you want push to work.

```powershell
./deploy/generate-vapid.ps1                 # Push__* environment-variable lines
./deploy/generate-vapid.ps1 -Format Json    # a "Push" block for appsettings
./deploy/generate-vapid.ps1 -Format UserSecret
```

The script wraps the Api's hidden `generate-vapid` argument
(`dotnet run --project src/NcaafPickEm.Api -- generate-vapid`), which prints a pair and exits
without touching the database or opening a port. Nothing is written to disk: paste the values into
`appsettings.Development.json`, user secrets, or the service's environment. `Push__Subject` must be
a real `mailto:` or `https:` contact — push services reject anything else.

**Keep the pair.** Replacing it invalidates every stored subscription, and every member has to turn
notifications on again. Never commit the private key.

### Notifications on iPhone

On iPhone/iPad, push only works from an app added to the Home Screen and opened from that icon
(iOS 16.4+); a regular Safari tab reports permission as denied and cannot receive push
(WorkItems/11-Notifications.txt). This has to be checked on a physical device — an operator step,
not something an agent in this environment can automate:

1. Set a real VAPID key pair on the server (`deploy/generate-vapid.ps1`, above) and confirm
   `GET /api/push/vapid-public-key` does not answer 503.
2. On the iPhone, open the deployed app's URL in Safari and sign in with Google.
3. Tap the **Share** icon in Safari's toolbar, then **Add to Home Screen**.
4. Open the app from its new Home Screen icon, not from Safari — this is what makes
   `navigator.standalone` true and unlocks the Notifications section on `/me` (otherwise it shows
   the "add to Home Screen first" instructions with the "Turn on" action hidden).
5. On the Profile page, tap **Turn on notifications** and accept the permission prompt.
6. As a league commissioner, trigger a test push: either the Development/Testing-only "Send test
   notification" button (built with `DefineConstants=USE_FAKE_API` unset, in a Debug client build
   talking to a Development/Testing Api) or `POST /api/push/test` directly.
7. Confirm the notification banner appears, and that tapping it brings the standalone app to the
   foreground (or launches it) at the target page rather than opening a new Safari tab.

Record the result in `Implementation/STATUS.md`'s P7-02 row.

## Google OAuth dev setup

Sign-in is ASP.NET Core cookie authentication plus the Google handler, with no Identity (D-004).
The app boots and every test passes **without** an OAuth client — the handler falls back to
placeholder credentials, and API tests replace Google's backchannel entirely. You only need the
steps below to click through a real Google login on your own machine.

1. Open the [Google Cloud console](https://console.cloud.google.com/), create (or pick) a project.
2. **APIs & Services -> OAuth consent screen**: user type *External*, fill in the app name and your
   own email, and add yourself under *Test users*. It can stay in *Testing*; publishing is not
   needed for a handful of family accounts.
3. **APIs & Services -> Credentials -> Create credentials -> OAuth client ID**, type
   *Web application*. Add the authorized redirect URI exactly:

   ```
   https://localhost:7092/auth/callback/google
   ```

   For the home server, add its Tailscale HTTPS hostname too:
   `https://<tailnet-host>/auth/callback/google`. Google requires HTTPS for anything but
   `localhost`, which is why the server needs a Tailscale-issued certificate.
4. Put the client ID and secret in user secrets — never in a file in the repo:

   ```bash
   dotnet user-secrets --project src/NcaafPickEm.Api set "Google:ClientId"     "<client-id>"
   dotnet user-secrets --project src/NcaafPickEm.Api set "Google:ClientSecret" "<client-secret>"
   ```

   (`appsettings.Development.json` works too and is gitignored; `Google__ClientId` /
   `Google__ClientSecret` environment variables work in production.)
5. Run with the `https` profile and visit <https://localhost:7092/login>:

   ```bash
   dotnet run --project src/NcaafPickEm.Api
   ```

   "Sign in with Google" does a full-page redirect to Google and comes back to
   `/auth/callback/google`, which upserts `Users` by Google subject, issues the `ncaaf.auth` cookie
   and redirects to `returnUrl`. `GET /api/me` then returns your account.

The cookie is `HttpOnly`, `Secure`, `SameSite=Lax`, named `ncaaf.auth`, with a 90-day sliding
expiration (Feature 08). `POST /auth/logout` clears it.

### Calling the API

- Unauthenticated `/api/*` returns **401 ProblemDetails**, never a redirect; the SPA handles it.
- Every mutating `/api` call must send `X-Requested-With: NcaafPickEm` or it is rejected with 400.
- League-scoped routes use `RequireLeagueMember()` / `RequireLeagueCommissioner()` on the group;
  the endpoint filter resolves `{leagueId}` once per request and handlers read it back with
  `HttpContext.GetMembership()`. Non-member is 404 (existence is not revealed); a member on a
  commissioner route is 403.

## Run the tests

```bash
dotnet test
```

- `NcaafPickEm.Domain.Tests` is pure: no database, no network.
- `NcaafPickEm.Api.Tests` boots the real app with `WebApplicationFactory<Program>`. `SqlTestDatabase`
  creates a throwaway database `NcaafPickEm_Test_<guid>`, migrates it, and drops it at the end of
  the run — one database per run, not per test, shared through the `ApiTestFixture` collection
  fixture. The server comes from the `TEST_SQL_CONNECTION` environment variable and defaults to
  LocalDB:

  ```bash
  TEST_SQL_CONNECTION="Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True"
  ```

  API tests run with `Jobs__Enabled=false` and `Providers__*=Fixture`.

No test ever talks to Google. `TestAuthHandler` registers a `TestAuth` scheme that signs a request
in as whatever user id the `X-Test-User` header names — use `ApiFactory.CreateClientAs(userId)` (or
`CreateMutatingClientAs`, which adds the CSRF header) after seeding rows with `TestUsers`. The
Google pipeline itself is covered by `AuthTests`, which swaps in `FakeGoogleBackchannel` and drives
the real challenge/callback round trip offline.

Every league-scoped endpoint group must have an authorization-matrix test. The harness is one call:

```csharp
await AuthMatrix.RunAsync(fixture, HttpMethod.Get, "/api/leagues/{leagueId}/x", "/api/leagues/{leagueId}/x/settings");
```

It seeds its own league, commissioner, member, and stranger, and asserts anonymous -> 401,
non-member -> 404, member on a commissioner route -> 403, commissioner -> 2xx.

Run a single project or a single test:

```bash
dotnet test tests/NcaafPickEm.Domain.Tests
dotnet test --filter "FullyQualifiedName~HealthEndpointTests"
```

## Formatting

`.editorconfig` is law (see `Implementation/05-Conventions.md`). Before committing:

```bash
dotnet format
dotnet format --verify-no-changes   # what CI / review checks
```

The build runs code-style analyzers (`EnforceCodeStyleInBuild`) with
`TreatWarningsAsErrors`, so an unused `using` fails the build rather than lingering.

## Database and EF Core migrations

The schema is `src/NcaafPickEm.Infrastructure/Data/AppDbContext.cs` plus one file per table in
`Data/Configurations/`. Entities live in `NcaafPickEm.Domain` and carry no EF attributes; all
mapping is Fluent. The whole schema from `Implementation/02-Data-Model.md` exists as of the
`Phase0_02_InitialSchema` migration.

Install the tool once (it must be at least as new as the EF Core packages in
`Directory.Packages.props`):

```bash
dotnet tool install --global dotnet-ef
dotnet tool update  --global dotnet-ef
```

Add a migration (append-only, named `Phase<N>_<Task>_<What>`; never edit one another task
committed):

```bash
dotnet ef migrations add Phase<N>_<Task>_<What> \
  --project src/NcaafPickEm.Infrastructure \
  --startup-project src/NcaafPickEm.Api \
  --output-dir Data/Migrations
```

Apply migrations by hand:

```bash
# bash
ConnectionStrings__Default="Server=(localdb)\\MSSQLLocalDB;Database=NcaafPickEm;Trusted_Connection=True;TrustServerCertificate=True" \
  dotnet ef database update --project src/NcaafPickEm.Infrastructure --startup-project src/NcaafPickEm.Api
```

```powershell
# Windows PowerShell
$env:ConnectionStrings__Default = "Server=(localdb)\MSSQLLocalDB;Database=NcaafPickEm;Trusted_Connection=True;TrustServerCertificate=True"
dotnet ef database update --project src/NcaafPickEm.Infrastructure --startup-project src/NcaafPickEm.Api
```

`NcaafPickEm.Infrastructure` contains an `IDesignTimeDbContextFactory<AppDbContext>`, so the tools
never boot the API host. It reads `ConnectionStrings__Default` and falls back to LocalDB, which is
why `dotnet ef migrations add` works on a clean clone with no configuration at all.

**Migrating on startup** (D-015): the app applies pending migrations at startup when
`Database__MigrateOnStartup` is true. The default is **on in Development and off everywhere else**,
so a developer never has to remember `database update` and a production deploy never migrates
itself by surprise. Set the key explicitly to override either way. Tests set it to false and
migrate their own throwaway database instead.

Generated migration files are exempt from the code-style analyzers through
`src/NcaafPickEm.Infrastructure/Data/Migrations/.editorconfig`. Do not hand-write code in that
folder.

## Publish

```bash
dotnet publish src/NcaafPickEm.Api -c Release -o <output>
```

One self-contained-by-framework folder holds the API and the Blazor app. `MapStaticAssets()` serves
the Web project's `wwwroot` and its `_framework` payload with fingerprinting and Brotli/gzip
negotiation. Do **not** add `UseBlazorFrameworkFiles()`: its private static-file branch bypasses the
endpoint middleware and returns 500 for every `/_framework` request once `MapStaticAssets()` owns
those routes.

## Deploy

The home server (P8-02) runs one Windows service, `NcaafPickEm`, behind a Tailscale-issued HTTPS
certificate, with SQL Server (Express is fine) on the same box. Everything below lives in
`deploy/`.

### First-time setup

1. **Install Tailscale** on the home server and join the family's tailnet
   (<https://tailscale.com/download>). Note the machine's tailnet hostname, e.g.
   `pickem.tailnet-1234.ts.net` (`tailscale status` shows it).
2. **Issue the HTTPS certificate**:

   ```powershell
   ./deploy/renew-cert.ps1 -TailnetHost pickem.tailnet-1234.ts.net -CertDir C:\NcaafPickEm\cert
   ```

   Writes `<host>.crt` / `<host>.key` (PEM) into `-CertDir`. Tailscale certs expire on the order
   of months; register the monthly renewal task once the service exists (step 5 below).
3. **Google OAuth**: add the redirect URI `https://<host>.<tailnet>.ts.net/auth/callback/google`
   and the JavaScript origin `https://<host>.<tailnet>.ts.net` to the OAuth client from "Google
   OAuth dev setup" above (or a separate production client - either works, they just need this
   redirect URI registered).
4. **Configure**: `cp deploy/.env.example deploy/.env` and fill in every value - see the comments
   in that file for what each key means. `Kestrel__Certificates__Default__Path`/`KeyPath` must
   point at the cert files from step 2; `App__PublicOrigin` and `ASPNETCORE_URLS`'s port must
   agree with each other and with the Google redirect URI's host. `deploy/.env` is gitignored;
   never commit it. `deploy/appsettings.Production.template.json` documents the same keys (plus
   the ones the app reads from `appsettings.json` instead of the environment, like `Kestrel` and
   `AllowedHosts`) for reference - it is not read directly; `install-service.ps1` writes `.env`'s
   keys straight into the service's own environment.
5. **Generate VAPID keys** (see "Web push (VAPID) keys" above) and put the pair into `deploy/.env`.
6. **Install the service**:

   ```powershell
   ./deploy/install-service.ps1
   ```

   Publishes Release (trimmed, Brotli), creates `C:\NcaafPickEm\{app,logs,backups}`, creates the
   `NcaafPickEm` Windows service pointed at the published exe, writes `.env`'s keys into the
   service's own registry environment (not the machine-wide environment), sets it to auto-restart
   on crash and start automatically on boot, starts it, and polls `/health/ready`. Idempotent -
   re-running it stops the service, republishes over the same folder, and starts it again.
7. **Register the recurring tasks** (SQL Server Express has no Agent, hence Task Scheduler):

   ```powershell
   ./deploy/register-backup-task.ps1
   ./deploy/register-renew-cert-task.ps1 -TailnetHost pickem.tailnet-1234.ts.net
   ```
8. **Run the first backup and verify it restores** (see "Backups" below) before calling the setup
   done.
9. From a phone joined to the same tailnet, open `https://<host>.<tailnet>.ts.net[:port]/` in
   Safari, sign in with Google, and Add to Home Screen.

### Redeploying (a code/config change already on `main`)

```powershell
./deploy/deploy.ps1
```

Runs `dotnet ef database update`, stops the service, republishes, starts it, and polls
`/health/ready` for up to 60 s (prints the last 20 log lines on failure). Refuses to run from
Saturday 10:00 ET through Sunday 03:00 ET - the game window (Feature 10) - unless you pass
`-Force`. `-WhatIf` prints every step, including whether the Saturday guard would currently block,
without touching anything.

### Tailscale HTTPS

- `tailscale cert <host>.<tailnet>.ts.net` (wrapped by `deploy/renew-cert.ps1`) writes a PEM
  cert+key pair; Kestrel's `Certificates:Default:Path`/`KeyPath` accept that pair directly (no
  `.pfx` conversion needed, since .NET 5). One cert covers every HTTPS endpoint Kestrel binds.
- The cert is only trusted by other devices on the same tailnet - that is the entire security
  model here; nothing is exposed to the public Internet.
- **Renewal**: `deploy/renew-cert.ps1` re-runs `tailscale cert` and restarts the service so Kestrel
  picks up the new files (it only reads them at startup). `deploy/register-renew-cert-task.ps1`
  schedules this every 4 weeks via Task Scheduler.
- Google OAuth redirect URI: `https://<host>.<tailnet>.ts.net/auth/callback/google`.
  `App__PublicOrigin` must match the scheme+host+port members see, since invite links
  (`InviteResponse.Url`) are built from it.

### Backups

- `deploy/backup.sql` runs `BACKUP DATABASE ... WITH CHECKSUM, INIT` into
  `deploy/.env`'s `BACKUP_FOLDER` (no `COMPRESSION`: that option is Standard/Enterprise-only and
  SQL Server Express rejects it outright - confirmed while validating this script against
  LocalDB, which reports as Express).
- `deploy/backup.ps1` runs it (via a resolved copy of `backup.sql`, not `sqlcmd -v` - see the
  comment in that script for why: this machine's `sqlcmd -v` mis-tokenizes any value containing a
  drive-letter colon) and prunes `.bak` files older than 30 days.
- `deploy/register-backup-task.ps1` schedules it daily at 03:45 local time via Task Scheduler
  (SQL Server Express has no SQL Server Agent).
- `deploy/restore-verify.ps1` restores the latest `.bak` into a throwaway
  `<DatabaseName>_RestoreCheck` database, runs `DBCC CHECKDB`, prints `Users`/`Leagues`/`Picks`
  row counts, then drops the scratch database. Run it once by hand after the first real nightly
  backup lands, and periodically afterwards - a green `BACKUP DATABASE` exit code is not proof a
  file is actually restorable.

## Operate

- **Data status page**: `/admin/data` (any commissioner) shows the last refresh attempt/success
  per data type, the CFBD monthly call counter, the live-score source and staleness banner, and a
  recent-jobs table. Use it first when something looks stale.
- **Corrections**: a commissioner can override a game's result or void it from the week
  configuration page (`/leagues/{id}/config/week/{week}`); scoring recomputes from there. There is
  no separate "corrections" surface.
- **Backups**: land nightly at 03:45 local in `BACKUP_FOLDER` (`deploy/.env`), retained 30 days.
  Confirm the file appears the morning after the first deploy, and periodically run
  `deploy/restore-verify.ps1` - see "Backups" above.
- **Saturday rules**: `deploy/deploy.ps1` refuses to run from Saturday 10:00 ET to Sunday 03:00 ET
  (the SaturdayPoller's own window) unless `-Force` is passed. Do not pass `-Force` on a normal
  Saturday; it exists for a genuine emergency fix.
- **Logs**: `logs/ncaaf-<date>.log` under the app's content root (`C:\NcaafPickEm\app\logs` with
  the layout above), daily rolling, 31 files retained. `deploy/deploy.ps1` prints the last 20 lines
  automatically when a redeploy's health check fails.
- **Service control**: `Get-Service NcaafPickEm`, `Restart-Service NcaafPickEm`,
  `Stop-Service NcaafPickEm`. Set to auto-restart on crash and start automatically on boot by
  `install-service.ps1`.

## Package versions

Central Package Management is on: every `PackageReference` in the repo is versionless and
`Directory.Packages.props` is the single source of truth. To add a package, add a `PackageVersion`
there and a versionless `PackageReference` in the consuming project. Do not bump an existing version
without a `DECISIONS.md` entry.

Notable pins: xUnit 2.9.3, FluentAssertions **7.2.2** (8.x changed its license — see D-009),
Serilog.AspNetCore 10.0.0.

## Logging

Serilog writes to the console and to a daily rolling file at `logs/ncaaf-<date>.log` relative to the
content root (31 files retained, 64 MB roll). `logs/` is gitignored. Sinks are declared in
`src/NcaafPickEm.Api/SerilogConfiguration.cs`; levels, overrides and enrichers come from the
`Serilog` section of `appsettings*.json`. Request logging is on via `UseSerilogRequestLogging()`.

## Where things plug in

Adding a feature should be a one-line change in each of these, never a rewrite of `Program.cs`:

| What you are adding | Where the one line goes |
|---|---|
| Any Infrastructure service (DbContext, provider, job, push) | `src/NcaafPickEm.Infrastructure/DependencyInjection.cs` → `AddInfrastructure` |
| Any API service (auth, validators, filters) | `src/NcaafPickEm.Api/DependencyInjection.cs` → `AddApiServices` |
| A new endpoint group | `src/NcaafPickEm.Api/Endpoints/EndpointMapping.cs` → `MapApiEndpoints`, plus your own `Endpoints/<Feature>Endpoints.cs` |
