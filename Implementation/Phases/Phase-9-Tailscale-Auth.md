# Phase 9 - Docker-only hosting, Tailscale identity, Google removal

Status: Plan agreed 2026-09-20 (Q1-Q11 answered). Not started.

Goals (from the owner):
1. Remove every Windows deployment script and every bit of Windows-service setup. The app runs in a
   Docker container on `dunn-home-server` or under Visual Studio debugging. Nothing else.
2. Remove Google sign-in completely.
3. Sign users in from the identity headers Tailscale Serve injects. The header is trusted as-is:
   this is a family app on a private tailnet and header spoofing is an accepted non-risk.

Depends on: Phase 8 merged (it is). Touches both repos: this one and `home-server`
(`services/ncaaf-pickem/`).

Read first: `01-Architecture.md` (Runtime shape), `Api/Auth/*`, `Hosting/ForwardedHeadersSetup.cs`,
`deploy/docker/compose.yaml`, home-server `services/ncaaf-pickem/README.md`.

---

## Open questions (answer before P9-01 starts)

| # | Question | Why it matters | Default if unanswered | Answer |
|---|----------|----------------|-----------------------|--------|
| Q1 | Has anyone signed in with Google on the deployed server yet? (`SELECT Email, GoogleSubject FROM Users` on `ncaaf-db`.) | Existing rows are keyed by Google's `sub`. Tailscale gives a login string, not a `sub`, so those rows would never match again. If rows exist, the migration must carry them over by email (`ExternalSubject = Email`), which is only correct for Google-invited tailnet users. | Assume no real rows. Migration is a pure column rename. | There is a singular test user. It can be ignored. |
| Q2 | Is `Tailscale-User-Login` acceptable as the user's `Email` everywhere the app shows it (`/api/me`, members list, invites)? For Google and Microsoft invitees it is their email. For a GitHub invitee it is `name@github`; for an Apple invitee it can be a private-relay address. | `Users.Email` is shown in the UI and there is no other email source once Google is gone. | Store the login verbatim in both `ExternalSubject` and `Email`. Accept the odd-looking values for non-Google accounts. | Google will be the most common invite. You can store the login verbatim. |
| Q3 | Sign-out: remove the button and `POST /auth/logout` entirely? Under header auth there is nothing to sign out of: the next request signs the same person straight back in. | Decides whether the cookie scheme survives in Production at all. | Remove both. Cookie scheme stays only for `/auth/dev-login` in Development/Testing. | Agree with recommendation |
| Q4 | Should the identity handler be always-on, or gated behind a config key like `Auth__TrustTailscaleHeaders`? Always-on means a `curl -H "Tailscale-User-Login: x"` against a VS debug session signs in as `x`. | The owner said spoofing does not matter, and always-on is actually useful in dev (a header-injecting browser extension gives a real-looking session). A flag is one more thing to set in compose. | Always-on, no flag. | Yes, always on |
| Q5 | `deploy/generate-vapid.ps1` is a Windows dev helper, not a deployment script. Delete it with the rest, or keep it? `dotnet run --project src/NcaafPickEm.Api -- generate-vapid` and `docker run --rm <image> generate-vapid` both do the same job. | It is referenced from README, `appsettings.Development.template.json`, and `AGENT-NOTES.md`. | Delete it. Docs point at `dotnet run -- generate-vapid`. | Agree |
| Q6 | With the Windows files gone, `deploy/` contains only `docker/`. Flatten `deploy/docker/*` to `deploy/*`? | home-server's compose header and README cite `deploy/docker/compose.yaml` as the upstream path, so flattening means edits there too. | Keep `deploy/docker/` as is. Less churn, and the name still says what it is. | Can leave it as is |
| Q7 | Do you want `Tailscale-User-Profile-Pic` stored and shown (avatars in members/leaderboard)? | New column, new UI. Out of scope unless asked. | Ignore the header. | Ignore it |
| Q8 | If a family member later joins the tailnet under a different identity provider, they become a brand-new user with no league memberships. Acceptable, or do you want a commissioner "merge/relink" tool? | Scope. | Acceptable. No tool. | No tool needed. |

---

## Design decisions already made (say so if you disagree)

- **Per-request header authentication, no session.** A `TailscaleAuthenticationHandler`
  (`AuthenticationHandler<AuthenticationSchemeOptions>`) reads `Tailscale-User-Login` on every
  request, upserts `Users` by that value, and returns the same claim shape
  `ExternalSignInService.CreatePrincipal` builds today. No cookie, no 90-day lifetime, no
  data-protection dependency for auth. One indexed `SELECT` per request is nothing at this scale.
  The alternative (use the header once to bootstrap the existing cookie) keeps more moving parts
  for no gain.
- **Default scheme is a policy scheme:** if `Tailscale-User-Login` is present and non-empty use
  the Tailscale handler, else fall through to the cookie scheme (which only `/auth/dev-login` ever
  writes). Tests keep `TestAuthHandler` as their default scheme exactly as now.
- **Empty or missing header is anonymous, never an error.** Tagged devices and Funnel traffic
  arrive with no identity headers; they get a 401 on `/api/*` and the informational `/login` page
  on a navigation.
- **`Users.GoogleSubject` becomes `Users.ExternalSubject`** (same length, same unique index). The
  Tailscale login is stored verbatim. Fixture users keep the `fixture:<name>` convention.
- **`Tailscale-User-Name` is decoded from RFC 2047** (`=?utf-8?q?...?=` and `?b?`) before it
  becomes the initial display name. Only utf-8 Q and B encodings are handled; anything else falls
  back to the email local part via `ToInitialDisplayName`.
- **`LastLoginUtc` is written on create and at most once per hour thereafter**, so the
  per-request handler does not turn every GET into an UPDATE.
- **`App__BehindProxy`, `App__PublicOrigin`, `DataProtection__KeysPath`, and the `keys` volume all
  stay.** Forwarded headers still feed the per-IP rate limiter; `PublicOrigin` still builds invite
  links; the key ring still protects the dev-login cookie and costs nothing in Production.
- **Append-only docs stay append-only.** `DECISIONS.md` gets new entries superseding D-004,
  D-101..D-105; `STATUS.md` gets a P9 row per item. Phase-0/Phase-8 docs are historical and are
  not rewritten; a one-line "superseded by Phase 9" note is added at the top of each affected
  section.

---

## P9-01 Remove the Windows deployment path
Tier: Sonnet. Depends on: nothing.

Deliverables
- Delete `deploy/install-service.ps1`, `deploy/deploy.ps1`, `deploy/renew-cert.ps1`,
  `deploy/register-renew-cert-task.ps1`, `deploy/backup.ps1`, `deploy/backup.sql`,
  `deploy/restore-verify.ps1`, `deploy/register-backup-task.ps1`, `deploy/.env.example`,
  `deploy/appsettings.Production.template.json`. (`deploy/generate-vapid.ps1`: per Q5.)
- `Program.cs`: remove `builder.Host.UseWindowsService()` and its comment.
- `NcaafPickEm.Api.csproj` + `Directory.Packages.props`: remove
  `Microsoft.Extensions.Hosting.WindowsServices` and the P8-02 comment.
- `README.md`: delete "Alternative: Windows service" (section 5), the `deploy.ps1` Saturday-guard
  sentences in "Updating" and "Operate", the `deploy/generate-vapid.ps1` references (per Q5),
  and the `install-service.ps1` layout note. "Publish" section: keep only what Docker uses.
- `01-Architecture.md`: remove the "Alternative: Windows service" paragraph and the `deploy/`
  tree lines for the deleted files.
- `reviews/operator-checklist.md`: drop item 5a.
- `screenshots/e2e/README.md`: replace "Deploy as usual (`deploy/deploy.ps1`)" with the Docker
  steps.
- `AGENT-NOTES.md`: fix the VAPID bullet (per Q5).
- `07-Traceability.md`: rows citing `deploy/backup.ps1` / `deploy.ps1` now cite
  `deploy/docker/backup.sh` and the watchtower Saturday note.

Done when
- `grep -rn -i "install-service\|deploy.ps1\|renew-cert\|register-.*-task\|UseWindowsService\|WindowsServices" --include=*.cs --include=*.md --include=*.props --include=*.csproj .` returns hits only inside `DECISIONS.md`, `STATUS.md`, and `Phases/Phase-8-*.md`.
- `dotnet build` 0 warnings, all tests green (nothing functional changes here).

## P9-02 Tailscale identity handler (server)
Tier: Opus. Depends on: P9-01 (so the auth setup is edited once).

Deliverables
- `Auth/AuthDefaults.cs`: add `TailscaleLoginHeader = "Tailscale-User-Login"`,
  `TailscaleNameHeader = "Tailscale-User-Name"`, `TailscaleScheme = "Tailscale"`,
  `SelectorScheme = "AppAuth"`. Remove `GoogleCallbackPath`.
- `Auth/ExternalLogin.cs`: doc-comment rewrite; shape unchanged (`Subject`, `Email`, `Name`).
- `Auth/ExternalSignInService.cs`: split into `UpsertAsync(ExternalLogin)` (public, returns `User`,
  throttles `LastLoginUtc` to once per hour) and the existing `SignInAsync` (cookie; now used only
  by dev-login). `CreatePrincipal(User, string scheme)` takes the scheme name.
- New `Auth/Rfc2047.cs`: `static string? Decode(string? value)` for `=?utf-8?q?..?=` and
  `=?utf-8?b?..?=`, multiple encoded words, pass-through otherwise. Unit-tested.
- New `Auth/TailscaleAuthenticationHandler.cs`: reads the two headers; empty login →
  `NoResult`; else `UpsertAsync` (scoped `AppDbContext` via `Context.RequestServices`) → `Success`.
  Never fails a request because of a malformed name header.
- `Auth/AuthenticationSetup.cs`: register `AddPolicyScheme(SelectorScheme)` as the default,
  forwarding to `TailscaleScheme` when the login header is present, else to the cookie scheme.
  Add the Tailscale scheme. Keep the cookie scheme with the same options minus `LoginPath`
  semantics that assumed a Google redirect (the 401/403 JSON behaviour under `/api` stays).
- `Domain/Users/User.cs`: rename `GoogleSubject` → `ExternalSubject`; doc-comment.
- `Infrastructure/Data/Configurations/UserConfiguration.cs`, `Seeding/FixtureSeeder.cs`,
  `Endpoints/DevAuthEndpoints.cs`: follow the rename.
- New migration `Phase9_01_ExternalSubject`: `RenameColumn` + `RenameIndex`. Per Q1, optionally a
  data step `UPDATE Users SET ExternalSubject = Email WHERE ExternalSubject NOT LIKE 'fixture:%'`.
- `Shared/Contracts/Auth/MeResponse.cs`: `Email` doc → "the Tailscale login".
- `03-API-Contracts.md` Auth section, `02-Data-Model.md` Users, `01-Architecture.md` runtime
  shape: rewrite for header identity.

Done when
- New `TailscaleAuthTests` (API tests, real handler, no `TestAuth` header): first request with a
  login header creates the user and `/api/me` returns it; second request matches by subject and
  does not duplicate; a changed `Tailscale-User-Name` does not overwrite an edited display name;
  RFC 2047 name decodes; >30-char name trimmed; missing header → 401; empty header → 401;
  header present on a `/auth/dev-login` request in Testing still works (policy scheme picks the
  right branch).
- `Rfc2047Tests` in Domain or Api tests (plain ASCII, Q with `_` for space, B, mixed, malformed).
- Existing suites still green with `TestAuthHandler` as default.

## P9-03 Remove Google sign-in (server, client, tests, config)
Tier: Sonnet. Depends on: P9-02.

Deliverables (server)
- `AuthenticationSetup.cs`: delete `ConfigureGoogle`, `OnGoogleTicketReceivedAsync`,
  `ReadExternalLogin`, both placeholder constants, the `AddGoogle` call and the `using`.
- `Endpoints/AuthEndpoints.cs`: delete `GET /auth/login/google`. Per Q3, delete `POST /auth/logout`
  too; if the group ends up empty in Production, delete `MapAuthEndpoints` and its call in
  `EndpointMapping.cs` (dev-login is mapped separately and keeps the `/auth` rate-limit policy).
- `Auth/ReturnUrl.cs`: keep (dev-login uses it). `ReturnUrlTests`: drop the Google case.
- `NcaafPickEm.Api.csproj` + `Directory.Packages.props`: remove
  `Microsoft.AspNetCore.Authentication.Google`.
- `appsettings.Development.template.json`: delete the `Google` block.
- `DependencyInjection.cs`, `ForwardedHeadersSetup.cs`, `Program.cs`, `Hosting/*`: fix comments
  that cite the Google `redirect_uri` as the reason for forwarded headers (the rate limiter and
  `PublicOrigin` are the surviving reasons).

Deliverables (client, `NcaafPickEm.Web`)
- `Pages/Auth/LoginPage.razor`: becomes an informational page, no button: "Open this app through
  Tailscale (https://dunn-home-server…:8443). Your tailnet account is your sign-in." Keep the
  `returnUrl` parameter so `UnauthorizedRedirectHandler` and `Join.razor` need no change.
- `Components/LogoutButton.razor` and its use in `Pages/Profile/ProfilePage.razor`: per Q3, delete.
- `wwwroot/service-worker.published.js`: keep the "server-owned paths are never cached" rule;
  reword the comment (`/auth/*` and `/api/*` are still server-owned).

Deliverables (tests)
- Delete `Infrastructure/FakeGoogleBackchannel.cs` and every Google case in `AuthTests.cs`
  (rename the file `DevLoginAndAnonymousTests.cs` or fold the remaining anonymous case into
  `TailscaleAuthTests`).
- `Infrastructure/ApiContractRoutes.cs`, `RouteInventoryTests.cs`: drop `GET /auth/login/google`
  (and `POST /auth/logout` per Q3).
- `CookieSecurityTests.cs`, `RateLimitTests.cs`, `ForwardedHeadersTests.cs`,
  `ProductionBehaviourTests.cs`: replace the Google-challenge probes with `/auth/dev-login` or a
  Tailscale-header request as appropriate; the assertions (cookie flags, 429 shape, forwarded
  scheme/host, dev-login absent in Production) all survive.
- `Infrastructure/TestUsers.cs`, `LeaderboardPerfTests.cs`: `GoogleSubject` → `ExternalSubject`
  (values can stay `google-…`; they are opaque).
- `Infrastructure/ApiFactory.cs`, `ApiTestFixture.cs`: remove the Google placeholder config.

Deliverables (deploy files in this repo)
- `deploy/docker/compose.yaml`, `compose.dev.yaml`, `deploy/docker/.env.example`: delete
  `Google__ClientId` / `Google__ClientSecret` and the `GOOGLE_*` keys; reword the
  `App__BehindProxy` comment.

Done when
- `grep -rn -i google src tests deploy Dockerfile .github` returns nothing.
- `dotnet build` 0 warnings; `dotnet format --verify-no-changes` clean; both test projects green.
- `docker compose -f deploy/docker/compose.dev.yaml up --build -d`, then
  `curl -H "Tailscale-User-Login: alice@example.com" -H "Tailscale-User-Name: Alice" http://127.0.0.1:5000/api/me`
  → 200 with a new user; `curl http://127.0.0.1:5000/api/me` → 401.

## P9-04 home-server repo and docs
Tier: Sonnet. Depends on: P9-03 merged and the image published.

Deliverables (`home-server/services/ncaaf-pickem/`)
- `docker-compose.yml`: remove the two `Google__*` lines; reword the header comment that says
  `App__BehindProxy` exists for the Google `redirect_uri`.
- `.env.example`: delete the "Google OAuth" block; the `PUBLIC_ORIGIN` comment loses its "must
  match the Google OAuth client" sentence.
- `README.md`: delete deploy step 3 (Google OAuth client), the two Google troubleshooting bullets,
  and "sign in with Google" in step 7; add one sentence: identity comes from
  `tailscale serve`'s `Tailscale-User-Login` header, so a tagged device or a non-tailnet request
  sees the "open through Tailscale" page.
- `SERVICES.md`: drop "Google OAuth redirect URI for the `:8443` origin" from the pending list.
- On the server: `docker compose up -d` after removing the two keys from `.env` (they are ignored
  if left, so this is tidy-up, not a cutover step).

Deliverables (this repo's docs)
- `README.md`: section 1 stack table (Auth row), section 2 runtime diagram (`/auth/*` line),
  section 3 "Google OAuth dev setup" → "Signing in locally" (dev-login, or a header-injecting
  browser extension), section 4 test notes (`FakeGoogleBackchannel` sentence), section 5 deploy
  steps 4 and 7, section 6 operate.
- `reviews/operator-checklist.md`: item 4 phone checks say "opens signed in as your tailnet
  account"; delete the Google OAuth item.
- `reviews/security-review.md`: append a dated note: header trust is deliberate, the boundary is
  the loopback-only port publish plus Tailscale Serve, and spoofing is an accepted non-risk for
  this deployment.
- `DECISIONS.md`: D-16x entries for Docker-only hosting, header identity, per-request auth with
  no session, `ExternalSubject` rename, RFC 2047 handling, removal of logout (per Q3).
- `STATUS.md`: P9-01..P9-04 rows; the phone-side check "signed in without a login prompt" stays
  `Manual pending (operator)` until done on a real phone.

Done when
- Owner opens `https://dunn-home-server.tail7d7633.ts.net:8443/` on a tailnet phone and lands
  on the picker signed in, with no login screen. Record the date in STATUS.md.

## P9-05 Six-digit invite codes and "join by code" on the home page
Tier: Sonnet. Depends on: P9-03 (so the home page is edited after the login page goes away).
Added 2026-09-20 at the owner's request. Q9-Q11 answered the same day.

What already exists (Feature 01, P1-01/P1-02), and stays
- `Invites` is already one row per code, each row bound to exactly one league: `Code` (unique
  index), `LeagueId`, `ExpiresUtc` (14 days), `MaxUses` (50), `Uses`, `RevokedUtc`.
- `GET /api/invites/{code}` previews (league name, season, member count, state) and
  `POST /api/invites/{code}/accept` joins, with the Revoked > Expired > AlreadyMember > Full
  priority (D-038) and former-member reactivation (D-037). Both sit behind the 20/min per-IP
  `invites` rate-limit policy.
- `/join/{code}` (`Pages/Leagues/Join.razor`) renders the preview card and the Join button.
- Commissioners create, list, share, and revoke codes on `/leagues/{id}/invites`.

So "each code is a unique invite to a league" is already the model. The new work is the code
format, the home-page entry point, and (per Q9) the per-person semantics.

Open questions for this item

| # | Question | Why it matters | Default if unanswered | Answer |
|---|----------|----------------|-----------------------|--------|
| Q9 | Is a code meant for one person (single use: `MaxUses = 1`, the commissioner generates one code per invitee), or does one code still serve the whole family (`MaxUses = 50` as today)? | Decides the default on create and the wording on the commissioner page ("Invite someone" vs "Create invite link"). A single-use code also makes "who joined with which code" meaningful. | Keep multi-use (50). A 6-digit code is easy enough to hand out once per league. | No preference; whichever is more straightforward with the current setup. → Keep multi-use (50): zero changes to `Invite`, `InviteService`, or the commissioner page's wording. |
| Q10 | Should the invite lifetime change? 14 days today. | A shorter life shrinks the guessable set; a longer one means fewer "ask for a new code" moments. | Keep 14 days. | Yes, keep 14 days. |
| Q11 | Should the "Join by code" box on the home page go straight to accept, or to the existing preview card first? | Preview first shows the league name and a Join button, so a mistyped code that happens to hit another league is caught. Straight-to-accept is one tap fewer. | Preview first: submit navigates to `/join/{code}`, which already does everything. No new API. | Straight to accept. The box calls `POST /api/invites/{code}/accept` itself; see the design bullet below. |

Design decisions already made (say so if you disagree)
- **Code format: exactly six digits, `0-9`, leading zeros allowed**, generated with
  `RandomNumberGenerator.GetInt32(1_000_000)` and formatted `D6`. The column stays `nvarchar(12)`
  so the one existing 8-character code (if any) keeps working; nothing migrates.
- **Uniqueness stays global** (the existing unique index across all invites, active or not). A
  million-code space with a handful of rows per season never collides in practice, and the
  existing five-attempt retry loop in `InviteService.GenerateUniqueCodeAsync` covers it anyway.
- **Guessability is accepted, and stated.** A 6-digit code behind the 20/min per-IP limit takes
  about 35 days of continuous guessing to hit one specific active code, and the caller has to be
  a signed-in tailnet member to reach the route at all. Same posture as Q4: the tailnet is the
  boundary. `RateLimitingSetup.cs`'s comment (which cites the 8-character, 31-symbol alphabet)
  is rewritten with the new numbers.
- **The home page becomes three actions**: (1) "Join by code": a six-digit input with
  `inputmode="numeric"`, `autocomplete="one-time-code"`, `pattern="[0-9]{6}"`, and a Join button;
  (2) "Create a league" (exists); (3) "Your leagues" (exists). The empty state ("No leagues yet")
  shows the same join box instead of the sentence about asking for a link.
- **The join box accepts directly (Q11).** Submit calls the existing
  `ILeaguesApi.AcceptInviteAsync(code)`, which already wraps `POST /api/invites/{code}/accept`.
  On success it navigates to `/leagues/{leagueId}` from the returned `LeagueDetail`. On a 409 it
  shows the same state copy `Join.razor` uses (AlreadyMember, Expired, Revoked, Full) inline under
  the box, and on a 404 it shows "No league found with that code." The state copy is lifted out of
  `Join.razor` into a small shared `InviteStateMessage` component so the two pages cannot drift.
  No new API, and no preview call from the home page.
- **`Join.razor` stays as the URL path.** `/join/{code}` keeps its preview card and Join button
  for links opened from a text message. It adopts the shared state component and gains a "Back to
  home" link on the non-Valid states.
- **Share text carries the code, not only the URL.** The commissioner's Share button sends
  "Join my NCAAF Pick Em league. Code: 123 456, or tap {url}" so a family member who opens the
  app from their home screen can just type the six digits. The code is shown grouped as `123 456`
  everywhere it is displayed (the API returns the raw six characters; the split is display-only).
- **URL joins keep working.** `/join/123456` is still the link in the share sheet; the header
  auth means it opens straight onto the preview card with no login detour.

Deliverables
- `Infrastructure/Services/InviteCodeGenerator.cs`: `Length = 6`, digits only, `D6` formatting;
  doc-comment rewritten.
- `Domain/Leagues/Invite.cs`: `DefaultMaxUses` (50) and `DefaultLifetime` (14 days) unchanged
  per Q9/Q10; doc-comment says "six-digit code".
- `Api/Auth/RateLimitingSetup.cs`: comment update with the new arithmetic.
- `Web/Pages/Leagues/LeaguePicker.razor` (+ `.css`): join-by-code card at the top that calls
  `AcceptInviteAsync` on submit, the three actions in the order above, the empty state rewired,
  a busy state on the button, and the inline outcome message. Client-side validation only says
  "enter the six-digit code"; the server stays the authority (a non-numeric path segment on
  `/api/invites/{code}` simply 404s as today).
- New `Web/Components/InviteStateMessage.razor`: renders the copy for each non-Valid
  `InviteState`, plus the "no league found" case; used by both pages.
- `Web/Pages/Leagues/Invites.razor`: code rendered as `123 456`; share text includes the code.
  Create button label unchanged (Q9).
- `Web/Pages/Leagues/Join.razor`: switch to `InviteStateMessage`; add a "Back to home" link on
  the non-Valid states so a mistyped code has an obvious way out.
- `Web/Services/LeaguesApi.cs` / `Fakes/FakeLeaguesApi`: `AcceptInviteAsync` already surfaces
  404 as `LeaguesApiException`; confirm the message is usable for the "no league found" case or
  add a `NotFound` outcome to `InviteAcceptOutcome`.
- `Shared/Contracts/Invites/InviteResponse.cs`: doc-comment (six digits).
- Tests: `InviteCodeGeneratorTests` (length 6, all digits, leading zero possible over a large
  sample, cryptographic source not asserted); `InviteAcceptTests` unchanged except any test that
  seeds a code by hand uses a six-digit one; a `LeaguePicker` note in `screenshots/` per the
  existing `p1-02-picker-*` convention if a screenshot run is part of the item.
- Docs: `03-API-Contracts.md` invites rows (code format); `02-Data-Model.md` Invites; `README.md`
  section 1 feature summary; `DECISIONS.md` D-16x for the six-digit decision and the accepted
  guessability; `STATUS.md` P9-05 row.

Done when
- A commissioner creates a code, reads six digits off the screen, and a second tailnet user
  types them on the home page, taps Join, and lands in the league with no intermediate screen.
  Typing the same code again shows "You're already a member" under the box; a made-up code shows
  "No league found with that code."
- `curl -H "Tailscale-User-Login: b@example.com" http://127.0.0.1:5000/api/invites/000123` on
  the dev stack returns the preview (leading zero preserved), and an 8-character legacy code, if
  one exists, still previews.
- Suite green, `dotnet format` clean.

## Phase exit criteria
- No file in either repo references Google, `install-service`, `deploy.ps1`, or Windows services
  outside the append-only history docs.
- A tailnet member reaches the app signed in on first open; a non-tailnet or tagged-device
  request gets the informational page and a 401 on `/api/*`.
- The home page offers join-by-code, create, and the league list; a six-digit code typed there
  lands the member in the right league.
- CI green; image on GHCR; watchtower rolled it out; nobody had to re-invite or re-join a league
  (or Q1 said there was nobody to preserve).
