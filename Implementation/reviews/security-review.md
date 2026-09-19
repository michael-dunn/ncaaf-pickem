# P8-01 - Security and correctness review

Branch `p8-01-security-review`, base `main @ 7e57e26`. Reviewer: Opus (`opus-p8-01`).
Folded in: the mandatory Opus review of **P5-02 (Corrections)**, which was merged without one, plus
the two open items the P7-03 review left for this task.

Suite at the start: 771 green (306 Domain, 465 Api). At the end of this branch's own work: 821
green (306 Domain, 515 Api). After merging `main` @ 969c461 (which brought P5-05 and P8-03):
**822 green (306 Domain, 516 Api)**, `dotnet build` 0 warnings,
`dotnet format --verify-no-changes` clean. P5-05 and P8-03 map no new endpoint, so the route
inventory below is unchanged by that merge.

---

## 1. Route inventory - every route in `03-API-Contracts.md`

**How.** `tests/NcaafPickEm.Api.Tests/RouteInventoryTests.cs` reads the booted app's own
`EndpointDataSource` (`Infrastructure/RouteInventory.cs` turns each `RouteEndpoint` into a
`RouteFact`) and asserts the security shape of every mapped endpoint. This is the durable version
of "walk every route": a new endpoint is covered the moment it is mapped.

An `IEndpointFilter` leaves no trace in `Endpoint.Metadata`, so the scoping helpers now stamp a
marker next to the filter they add (D-157): `EndpointScopeMetadata` from `RequireLeagueMember()` /
`RequireLeagueCommissioner()` / `RequireAnyLeagueCommissioner()`, and `CsrfProtectedMetadata` from
the new `RequireCsrfHeader()` (which the `/api` group in `EndpointMapping.cs` now calls instead of
`AddEndpointFilter<CsrfEndpointFilter>()` - same filter, one extra line of metadata).

**Assertions (all green).**

| Test | Asserts |
|---|---|
| `...ThenEveryApiRouteIsAuthorized` | every `/api` endpoint carries an authorization policy and none is `AllowAnonymous` |
| `...ThenEveryLeagueScopedRouteCarriesTheMembershipFilter` | every `/api/leagues/{leagueId}/**` endpoint is scoped `LeagueMember` or `LeagueCommissioner` |
| `...ThenEveryMutationIsUnderTheCsrfGroup` | every mutating `/api` endpoint is covered by `CsrfEndpointFilter` |
| `...ThenOnlyTheAllowListIsAnonymous` | the only anonymous routes are `/health`, `/health/ready`, `/auth/login/google`, `/auth/dev-login` (Development/Testing only) |
| `...ThenNothingMutatingIsServedOutsideApi` | no mutation outside `/api`, except the documented `POST /auth/logout` |
| `...ThenEveryContractRouteIsPresent` | every route table row in `03-API-Contracts.md` (transcribed by hand into `Infrastructure/ApiContractRoutes.cs`) is actually mapped |

**Evidence - the inventory as the test prints it** (`POLICY` is every authorization policy on the
endpoint, `SCOPE` the marker, `CSRF` whether the filter covers it):

```text
METHOD(S) | ROUTE | POLICY | SCOPE | CSRF
GET | /api/admin/data-status | Authenticated | AnyLeagueCommissioner | yes
GET | /api/admin/fixture/snapshot | Authenticated | - | yes
POST | /api/admin/fixture/snapshot/{n:int} | Authenticated | - | yes
POST | /api/admin/refresh/{dataType} | Authenticated | AnyLeagueCommissioner | yes
POST | /api/admin/unmatched/{id:guid}/resolve | Authenticated | AnyLeagueCommissioner | yes
GET | /api/invites/{code} | Authenticated | - | yes
POST | /api/invites/{code}/accept | Authenticated | - | yes
GET | /api/leagues/ | Authenticated | - | yes
POST | /api/leagues/ | Authenticated | - | yes
GET | /api/leagues/{leagueId:guid}/ | Authenticated | LeagueMember | yes
GET | /api/leagues/{leagueId:guid}/audit | LeagueMember | LeagueMember | yes
POST | /api/leagues/{leagueId:guid}/commissioner/transfer | Authenticated | LeagueCommissioner | yes
GET | /api/leagues/{leagueId:guid}/gameset-rules | LeagueCommissioner | LeagueCommissioner | yes
PUT | /api/leagues/{leagueId:guid}/gameset-rules | LeagueCommissioner | LeagueCommissioner | yes
GET | /api/leagues/{leagueId:guid}/invites/ | LeagueCommissioner | LeagueCommissioner | yes
POST | /api/leagues/{leagueId:guid}/invites/ | LeagueCommissioner | LeagueCommissioner | yes
DELETE | /api/leagues/{leagueId:guid}/invites/{inviteId:guid} | LeagueCommissioner | LeagueCommissioner | yes
GET | /api/leagues/{leagueId:guid}/leaderboard | LeagueMember | LeagueMember | yes
GET | /api/leagues/{leagueId:guid}/members | Authenticated | LeagueMember | yes
PUT | /api/leagues/{leagueId:guid}/members/me/display-name | Authenticated | LeagueMember | yes
DELETE | /api/leagues/{leagueId:guid}/members/{membershipId:guid} | Authenticated | LeagueCommissioner | yes
POST | /api/leagues/{leagueId:guid}/members/{membershipId:guid}/demote | Authenticated | LeagueCommissioner | yes
POST | /api/leagues/{leagueId:guid}/members/{membershipId:guid}/promote | Authenticated | LeagueCommissioner | yes
GET | /api/leagues/{leagueId:guid}/notifications/log | LeagueCommissioner | LeagueCommissioner | yes
GET | /api/leagues/{leagueId:guid}/point-rules | LeagueCommissioner | LeagueCommissioner | yes
PUT | /api/leagues/{leagueId:guid}/point-rules | LeagueCommissioner | LeagueCommissioner | yes
PUT | /api/leagues/{leagueId:guid}/settings | Authenticated | LeagueCommissioner | yes
GET | /api/leagues/{leagueId:guid}/weeks | Authenticated | LeagueMember | yes
GET | /api/leagues/{leagueId:guid}/weeks/{week:int}/dashboard | LeagueMember | LeagueMember | yes
GET | /api/leagues/{leagueId:guid}/weeks/{week:int}/gameset | LeagueMember | LeagueMember | yes
GET | /api/leagues/{leagueId:guid}/weeks/{week:int}/gameset-rules | LeagueCommissioner | LeagueCommissioner | yes
PUT | /api/leagues/{leagueId:guid}/weeks/{week:int}/gameset-rules | LeagueCommissioner | LeagueCommissioner | yes
POST | /api/leagues/{leagueId:guid}/weeks/{week:int}/gameset/games | LeagueCommissioner | LeagueCommissioner | yes
DELETE | /api/leagues/{leagueId:guid}/weeks/{week:int}/gameset/games/{gameId:guid} | LeagueCommissioner | LeagueCommissioner | yes
POST | /api/leagues/{leagueId:guid}/weeks/{week:int}/gameset/games/{gameId:guid}/override-result | LeagueCommissioner | LeagueCommissioner | yes
PUT | /api/leagues/{leagueId:guid}/weeks/{week:int}/gameset/games/{gameId:guid}/points | LeagueCommissioner | LeagueCommissioner | yes
POST | /api/leagues/{leagueId:guid}/weeks/{week:int}/gameset/games/{gameId:guid}/void | LeagueCommissioner | LeagueCommissioner | yes
POST | /api/leagues/{leagueId:guid}/weeks/{week:int}/gameset/generate | LeagueCommissioner | LeagueCommissioner | yes
POST | /api/leagues/{leagueId:guid}/weeks/{week:int}/gameset/preview | LeagueCommissioner | LeagueCommissioner | yes
GET | /api/leagues/{leagueId:guid}/weeks/{week:int}/grid | LeagueMember | LeagueMember | yes
GET | /api/leagues/{leagueId:guid}/weeks/{week:int}/leaderboard | LeagueMember | LeagueMember | yes
GET | /api/leagues/{leagueId:guid}/weeks/{week:int}/picks | LeagueMember | LeagueMember | yes
GET | /api/leagues/{leagueId:guid}/weeks/{week:int}/picks/me | LeagueMember | LeagueMember | yes
POST | /api/leagues/{leagueId:guid}/weeks/{week:int}/picks/me/ack-changes | LeagueMember | LeagueMember | yes
POST | /api/leagues/{leagueId:guid}/weeks/{week:int}/picks/me/submit | LeagueMember | LeagueMember | yes
PUT | /api/leagues/{leagueId:guid}/weeks/{week:int}/picks/me/{gameId:guid} | LeagueMember | LeagueMember | yes
GET | /api/leagues/{leagueId:guid}/weeks/{week:int}/picks/status | LeagueCommissioner | LeagueCommissioner | yes
GET | /api/me/ | Authenticated | - | yes
PUT | /api/me/ | Authenticated | - | yes
GET | /api/push/status | Authenticated | - | yes
DELETE | /api/push/subscriptions | Authenticated | - | yes
POST | /api/push/subscriptions | Authenticated | - | yes
POST | /api/push/test | Authenticated | AnyLeagueCommissioner | yes
GET | /api/push/vapid-public-key | Authenticated | - | yes
GET | /api/reference/conferences | Authenticated | - | yes
GET | /api/reference/teams | Authenticated | - | yes
GET | /api/seasons/{year:int}/weeks | Authenticated | - | yes
GET | /api/seasons/{year:int}/weeks/{week:int}/games | Authenticated | AnyLeagueCommissioner | yes
GET | /auth/dev-login | (none) | - | no
GET | /auth/login/google | (none) | - | no
POST | /auth/logout | Authenticated | - | no
GET | /health | (none) | - | no
GET | /health/ready | (none) | - | no
```

(A trailing slash on `/api/leagues/`, `/api/me/` and `/api/leagues/{leagueId:guid}/` is how a group
mapped with `""` renders its own template; the routes answer without it.)

**Contract cross-check.** Every route table row in `03-API-Contracts.md` is mapped; nothing in the
contract is missing and nothing under `/api` is unlisted except the Development-only
`/api/admin/fixture/*` and `/api/push/test`, both of which `ProductionBehaviourTests` proves are not
mapped in Production. `/auth/callback/google` correctly has no endpoint - it is the Google handler's
`CallbackPath`, answered by the authentication middleware before routing runs.

**Observation, not a finding.** `POLICY` often reads `Authenticated` where `SCOPE` reads
`LeagueMember`: a feature group applies `RequireAuthorization(PolicyNames.Authenticated)` at group
level and the endpoint then adds its own. All three policies assert only `RequireAuthenticatedUser`
(`PolicyNames`' own doc comment says so - the league scope cannot be a policy because it depends on
a route value), so this is cosmetic. The scope column, and the HTTP probes in §2, are what prove
the real check.

---

## 2. Authorization matrix - every group, not one route per group

`tests/NcaafPickEm.Api.Tests/GeneratedAuthMatrixTests.cs` drives the same inventory over HTTP, so
no group in `03-API-Contracts.md` can be missed and none has to be remembered:

| Test | Routes probed | Expected |
|---|---|---|
| `GivenAnAnonymousCaller_WhenCallingEveryApiRoute_ThenEveryOneIs401` | every `/api` route | 401 |
| `GivenAStranger_WhenCallingEveryLeagueScopedRoute_ThenEveryOneIs404` | every `LeagueMember`/`LeagueCommissioner` route | 404 (never reveal existence) |
| `GivenAPlainMember_WhenCallingEveryCommissionerRoute_ThenEveryOneIs403` | every `LeagueCommissioner` route | 403 |
| `GivenACallerWhoCommissionsNothing_WhenCallingEveryAdminRoute_ThenEveryOneIs403` | every `AnyLeagueCommissioner` route | 403 (D-035) |
| `GivenASignedInCaller_WhenMutatingEveryApiRouteWithoutTheCsrfHeader_ThenEveryOneIs400` | every `/api` mutation | 400 |

These assert only the refusal side, which the policy and the filters decide before any handler runs
and therefore needs no per-route data. The success side stays in the hand-written per-group
matrices (`AuthMatrixTests`, `GameSetAuthMatrixTests`, `PicksAuthMatrixTests`,
`LeaderboardAuthMatrixTests`, `CorrectionsAuthMatrixTests`, `DashboardTests`), which seed what each
route needs. **No group was missing a matrix; the generated pass adds coverage for the groups that
previously had only an indirect one (season weeks, reference, admin, push, me, invites, the
notifications log).**

**Worth knowing (found by the generated matrix).** Minimal-API model binding runs *ahead* of the
endpoint filters that decide 404/403, so `PUT .../gameset-rules`, `PUT .../point-rules` and
`POST .../gameset/preview` (all of which bind a JSON **array**) answer 400 to a stranger when the
body is the wrong shape, rather than 404. Not an information leak - the 400 comes from the body and
is identical whether or not the league exists - but it is why those routes are probed with both
`{}` and `[]` and must refuse the caller for one of them.

---

## 3. CSRF, cookie flags, `returnUrl`, ProblemDetails

### CSRF
`CsrfEndpointFilter` is applied once to the whole `/api` group and keys on the HTTP method, not on
whether a body was sent. `CsrfTests` now covers: mutation with no header (400), mutation with the
wrong value (400), a read with no header (200), **a `DELETE` carrying a body** (400,
`DELETE /api/push/subscriptions`), **an `/api/admin/*` mutation** (400), and `/health` at the root
being exempt. The inventory test proves coverage structurally; `GeneratedAuthMatrixTests` proves it
over HTTP for every mutation the app maps.

`POST /auth/logout` is deliberately outside the group (03-API-Contracts.md): the session cookie is
`SameSite=Lax`, which a cross-site form post never carries.

### Cookie flags
`CookieSecurityTests` checks the Feature 08 contract twice over.

* From configuration (`IOptionsMonitor<CookieAuthenticationOptions>`): name `ncaaf.auth`,
  `HttpOnly = true`, `SecurePolicy = Always`, `SameSite = Lax`, `ExpireTimeSpan = 90 days`,
  `SlidingExpiration = true`.
* From the wire, after a real Google sign-in through the shipped pipeline: the `Set-Cookie` header
  carries `httponly`, `secure`, `samesite=lax` and an `expires=` (persistent, so the phone survives
  a restart).

### 401 for `/api`, redirect elsewhere
`GET /api/me` anonymous is **401**, `application/problem+json`, no `Location` header. `POST
/auth/logout` anonymous is **302 to `/login`** on the same origin - the browser-facing branch of
`OnRedirectToLogin`. Both in `CookieSecurityTests`.

### `returnUrl` is relative-only
`ReturnUrlTests` covers absolute (`http`/`https`, and mixed-case `HTTPS:`/`Https:`),
protocol-relative `//evil`, `/\evil`, `\\evil`, `\/evil`, leading whitespace, bare relative
(`leagues/mine`), `javascript:` (any case), `data:`, null/empty/whitespace, and percent-encoded
`%2F%2F` / `%2f%2f` / `%2F%5C` in their decoded form (query binding decodes before `Sanitize` ever
sees the value). `AuthTests` proves the end-to-end behaviour through the real challenge/callback:
an absolute `returnUrl` lands on `/`, a relative one is honoured.

**Finding, fixed.** `Sanitize` accepted a candidate with a control character in the middle
(`"/leagues\r\nSet-Cookie: stolen=1"`): it starts with one slash, so it passed. That value goes
straight into the `Location` header - response splitting in principle, and in practice a Kestrel
`InvalidOperationException` that would 500 the login. `ReturnUrl.Sanitize` now refuses any candidate
containing a control character. Commit `9f02035`.

### ProblemDetails in Production
`ProductionBehaviourTests` boots the real host with `ASPNETCORE_ENVIRONMENT=Production` (real
Program.cs pipeline, the run's database) and forces a genuine unhandled exception inside a real
endpoint by registering a `LeagueService` factory that throws with a distinctive message. The
response is **500 `application/problem+json`** whose body contains none of: the exception message,
the string `InvalidOperationException`, any `NcaafPickEm.` stack frame, or a `stackTrace` property.
`UseExceptionHandler()` + `UseStatusCodePages()` + `AddProblemDetails()` behave as intended, and
`Program.cs` never adds a developer exception page.

The same test proves the Development-only routes are **not mapped** in Production:
`/auth/dev-login`, `/api/admin/fixture/*`, `/api/push/test`, while `/api/me` and `/health/ready`
still are.

---

## 4. The three "never" rules

### (1) No picks visible before lock - **holds**
`NeverRulesTests` takes one unlocked week with two members' real picks on it and walks every read:

| Surface | Before lock |
|---|---|
| `GET .../weeks/{week}/picks` | 403, ProblemDetails title `PicksNotVisible` |
| `GET .../weeks/{week}/grid` | 403, same title |
| `GET .../weeks/{week}/dashboard` | 200, `IsAvailable=false`, `Games` and `EveryoneAgrees` empty, `PointsSoFar`/`MaxRemaining` 0 (D-116) |
| `GET .../weeks/{week}/picks/status` (commissioner) | 200 by contract - and the **raw JSON contains no team id at all**, no `teamId` property and no `gameSetGameId`; `MemberStatusRow` is statuses and counts only |
| `GET .../weeks/{week}/picks/me` | 200, only the caller's own pick; no other membership id appears in the body |

Pre-existing coverage (`PicksVisibilityTests`, `LeaderboardEndpointsTests`, `DashboardTests`) is
unchanged; what was missing was one place that asserts all five at once on the same data.

### (2) No mutations after lock - **holds**
Every week-scoped mutation is refused from `LockAtUtc`, not merely from `LockedUtc` (D-110), through
the single `WeekGameSetLockGuard`. `PostLockMutationTests` covers `generate`, manual `add`, manual
`remove`, week `gameset-rules` and the per-game point `override`, each twice (job has run / lock
instant passed but job has not), plus `picks/me/{gameId}` and `picks/me/submit`, plus
`PUT .../point-rules` leaving a frozen week's values untouched. `LockEnforcementTests` adds the
boundary (one second before lock still succeeds).

Walking the inventory against that list, **no week-scoped mutation is missing** a case. The routes
that legitimately are not in it:

* `PUT /api/leagues/{leagueId}/gameset-rules` (league default) - save-only, not week-scoped.
* `POST .../gameset/preview` - a pure projection that writes nothing.
* `POST .../picks/me/ack-changes` - clears the caller's own "new games" flag; a read-side
  acknowledgement, correctly allowed on a locked week.

Override and void are the mirror image and are refused **before** lock: `OverrideTests` and
`VoidTests` each have a `GivenTheWeekHasNotLocked_...ThenItIs409` case (`NotLocked`).

### (3) No `DateTime.UtcNow` in the Domain - **holds, zero hits**

```text
$ grep -rn "DateTime\.UtcNow\|DateTime\.Now\|DateTimeOffset\.UtcNow\|DateTimeOffset\.Now" src/
src/NcaafPickEm.Domain/         -> 0 hits
src/NcaafPickEm.Infrastructure/ -> 0 hits (excluding Data/Migrations, which is EF-generated)
src/NcaafPickEm.Api/            -> 0 hits
src/NcaafPickEm.Shared/         -> 0 hits
src/NcaafPickEm.Web/            -> 13 hits
```

Domain, Infrastructure and Api are **completely clean** - every clock read goes through
`TimeProvider`. The 13 Web hits are all legitimate and none is a candidate for `TimeProvider`:

* `Components/LockTime.razor`, `Pages/Admin/DataStatusPage.razor`,
  `Pages/Dashboard/DashboardPage.razor`, `Pages/More.razor` - rendering a countdown, an "N minutes
  ago" and a local-time label in the browser. The client has no injected clock and deliberately
  renders the *viewer's* wall clock (D-023/D-045); a server-supplied instant would be the wrong
  answer here.
* `Services/Fakes/*` (`FakeAdminApi`, `FakeGameSetStore`, `FakeLeaguesApi`, `FakePicksApi`) -
  Development-only fakes behind `DEBUG && USE_FAKE_API` (D-043/D-044), never in a Release build.

Test code uses `DateTime.UtcNow` freely for seeding; that is not in scope for the rule.

---

## 5. Rate limiting (D-153)

Added: `Api/Auth/RateLimitingSetup.cs`, one fixed window per client IP per policy, one minute wide.

| Policy | Routes | Default |
|---|---|---|
| `auth` | the whole `/auth` group (`/auth/login/google`, `/auth/logout`) | 30 / minute |
| `invites` | `/api/invites/{code}`, `/api/invites/{code}/accept` | 20 / minute |

Refusals are **429 `ProblemDetails`** with `Retry-After`; `UseRateLimiter` sits after
`UseStatusCodePages` in `Program.cs` so the bare 429 is rendered the same way the cookie handler's
401 and 403 are. No queueing (`QueueLimit = 0`). Keys: `RateLimiting__Enabled` (default true),
`RateLimiting__AuthPermitPerMinute`, `RateLimiting__InvitePermitPerMinute` - all documented in
`deploy/.env.example` and `deploy/appsettings.Production.template.json`.

These are the only two families worth limiting, and the route inventory is why: every other `/api`
endpoint needs a session cookie and, on a league route, an active membership resolved by a filter
before the handler. `/auth/*` is reachable with no cookie at all, and an 8-character invite code
from a 31-character alphabet is the one secret a signed-in stranger could guess at (20 guesses a
minute is not a keyspace search; a real person redeems one invite).

**Testing.** `ApiFactory` sets `RateLimiting:Enabled=false` - every `WebApplicationFactory` request
has no remote IP, so the whole suite would share one partition. `RateLimitTests` boots its own host
with the limiter on and a three-per-minute window: sign-in over the limit is 429 + `Retry-After` +
`application/problem+json`, invite-code guessing over the limit is 429, and an unlimited route
(`GET /api/me`) called nine times in a row is never refused.

---

## 6. Dependency audit

```text
$ dotnet list package --vulnerable --include-transitive
  NcaafPickEm.Api            - no vulnerable packages given the current sources
  NcaafPickEm.Domain         - no vulnerable packages given the current sources
  NcaafPickEm.Infrastructure - no vulnerable packages given the current sources
  NcaafPickEm.Shared         - no vulnerable packages given the current sources
  NcaafPickEm.Web            - no vulnerable packages given the current sources
  NcaafPickEm.Api.Tests      - no vulnerable packages given the current sources
  NcaafPickEm.Domain.Tests   - no vulnerable packages given the current sources
  NcaafPickEm.Fixtures       - no vulnerable packages given the current sources
```

**Nothing to fix; `Directory.Packages.props` is untouched.**

`dotnet list package --deprecated --include-transitive` reports only `xunit` 2.9.3 and its
transitive `xunit.*` packages as `Legacy` (alternative `xunit.v3`). AGENT-NOTES pins xunit 2.9.3 on
purpose; a v3 migration is a test-infrastructure project of its own and is **not** a security issue.

`dotnet list package --outdated` shows the ASP.NET/EF packages at 10.0.11 with 10.0.12 available.
AGENT-NOTES requires the package versions to match the installed runtime (10.0.11); bumping them
here would mismatch the box. **Left alone deliberately** - flagged for P8-02/P8-04 to bump package
and runtime together at deploy time. `FluentAssertions` stays on 7.2.2 (AGENT-NOTES: do not upgrade
to 8); `coverlet.collector`, `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` have newer
majors with no advisory attached.

---

## 7. Secrets

| Check | Result |
|---|---|
| CFBD key prefix the key's first characters anywhere in the repo | **0 hits** |
| `Bearer ` literal | 1 hit, a doc comment in `CfbdLiveTests.cs`; no token |
| `VapidPrivateKey` | only config keys, placeholders (`<vapid-private-key>`) and generator code; `WebPushSenderTests` generates a pair at runtime |
| Connection strings / `ClientSecret` / API-key literals | 0 hits outside placeholders and the deliberate `...-not-configured` Google placeholders in `AuthenticationSetup` |
| Tracked files matching `.env`, `secret`, `*.pfx`, `*.key`, `appsettings.{Development,Production,Local}.json` | only `deploy/.env.example`, which is placeholders by design |

`.gitignore` covers `appsettings.Development.json`, `appsettings.Production.json`,
`appsettings.Local.json`, `secrets.json`, `*.pfx`, `*.key`, `.env` / `.env.*` (with
`!.env.example`) and `logs/`. `deploy/.env` is matched by the pathless `.env` rule.

---

## 8. Folded-in review of P5-02 (Corrections)

Reviewed `git diff 48fa8be 7e57e26 -- . ':(exclude)Implementation'` (15 files, ~1 400 lines).

**Correct as built** - verified by reading and by the existing tests:

* Override and void are refused before lock (`WeekGameSetLockGuard.IsFrozen`, 409 `NotLocked`) and
  load through one `LoadLockedRowAsync`, which also runs `WeekRangeGuard` (404 `WeekOutOfRange`)
  and 404s a row that does not exist or was manually removed.
* `WinnerTeamId` must be the game's home or away team (400 `TeamNotInGame`).
* A voided row cannot be overridden (409 `AlreadyVoided`); a void is one-way.
* Audit rows carry the actor membership, the action, the `WeekGameSetGames.Id` as `TargetId`, and
  `Details` with `gameId`/`before`/`after`/`reason`/`homeScore`/`awayScore` (D-146).
* Events are raised **after** `SaveChangesAsync` (the collector is drained first, then saved, then
  dispatched - same guarantee: a subscriber sees a database that already agrees), and P5-01's
  `ResultOverriddenScoringHandler` / `GameVoidedScoringHandler` are registered, so rescoring does
  happen (`OverrideTests` asserts the tie resolves and the week completes; `VoidTests` asserts the
  voided game scores nothing and drops out of `ActiveGameCount`).
* The audit is visible to members only (`RequireLeagueMember()`; `CorrectionsAuthMatrixTests` gives
  a stranger 404), and the two mutations are commissioner-only.
* The needs-review list updates: an overridden tie disappears from `GET /api/admin/data-status`
  (`OverrideTests`).
* No scoring logic was added here, per the seam P5-01 defined.

**Two defects found and fixed (D-156, commit `a748ad5`).**

1. **`GetAuditAsync` could 500 the whole league's audit page.** `AuditSummaryBuilder` read
   `details.GetProperty("after")` and `details.GetProperty("week")`, and called `TryGetProperty` on
   whatever `Details` parsed to. `GetProperty` throws `KeyNotFoundException`; `TryGetProperty`
   throws `InvalidOperationException` on a non-object. Neither is a `JsonException`, so neither was
   stopped by the builder's own `catch`. One row whose `Details` was valid JSON of an unexpected
   shape - `{}`, `[]`, `null`, a `week` that is a string - returned 500 to every member.
   Today's writers all produce the expected shape, so this was latent, but `Details` is an
   unstructured blob with writers in five different tasks. Every reader is now total.
   `CorrectionEdgeCaseTests` drives six malformed shapes through the real endpoint.
2. **`ListNeedsVoidReviewAsync` listed rows that cannot be acted on.** The query filtered
   `!IsVoided` and `ResultOverrideWinnerTeamId == null` but not `!IsRemoved`, so a game removed
   before lock that was later postponed appeared on the data-status "needs review" list - while
   both actions that list offers answer 404 `GameNotFound`, because `CorrectionService` filters
   `!IsRemoved`. **P5-05's one-tap void reads this list**, so this would have shipped as a dead
   button. Fixed and covered.

**Reviewed and left alone.** D-143 (a repeated identical override re-audits and re-raises rather
than being swallowed) is a documented judgement call and stands: the rescore is deterministic and
an honest second audit line is better than the service guessing. Voiding an already-voided row
stays a 409.

---

## 9. P7-03's open items

Both closed.

* **GamesAdded re-fire (D-155).** `SubmittedUtc` is never cleared, so the handler's
  `SubmittedUtc < OccurredUtc` test stayed true forever: a member who ignored the first add was
  notified again by the second, the third, and every regeneration for the rest of the week. The
  handler now compares `SubmittedUtc` against the newest `AddedUtc` among the rows that were
  *already* in the set - `SubmissionStatusCalculator`'s own definition of Submitted, evaluated
  against the set as it stood a moment earlier. It reads `Status` not at all, so it is more
  order-independent of P4-04 than before, and a member who re-submits is notified again (submit
  refreshes `SubmittedUtc` whenever the status is not already Submitted).
  `EventNotificationTests` gains both cases.
* **Off-season guard (D-154).** `SeasonCalendar.CurrentWeekAt` clamps to the first or last week
  outside a week window, and neither clamp says "there is no current week", so a league whose final
  week was never locked drew a Friday reminder every week of the summer.
  `ReminderRecipients.CurrentUnlockedSetsAsync` now skips a season year whose state is not
  `InSeason`; `ReminderJobTests` has a before-season / after-season / in-season theory. The
  Saturday one-shot needs no equivalent guard - its due time is a real `LockAtUtc` row.
* **Catalog #2 pluralisation** (the third item on that list) is a copy question, not a correctness
  or security one. **Left for P8-04** with the rest of the catalog wording.

---

## 10. Accepted risks re-checked

| Item | Where | Verdict |
|---|---|---|
| Last-commissioner race between two concurrent demote/remove calls; `Invite.Uses` incremented with no optimistic-concurrency check | STATUS Escalations, 2026-09-18 | **Still accepted.** Both need two commissioners of the same 2-50 person family league acting within the same second, and the worst outcome is a league briefly without a commissioner (recoverable by the operator) or one extra member over the cap. Revisit only if untrusted multi-tenant leagues ever appear. |
| `/api/admin/fixture/*` requires only `Authenticated`, not a commissioner, unlike the rest of `/api/admin/*` | inventory row, §1 | **Accepted.** Development/Testing only - `ProductionBehaviourTests` proves it is never mapped in Production - and its whole effect is stepping a fixture score snapshot. Tightening it to `RequireAnyLeagueCommissioner()` would change what several existing tests can call for no production benefit. |
| Unmatched-resolve picker is a raw GUID box (P2-04 review) | STATUS P2-04 note | Out of scope here; still a UI item for P8-04 polish. |
| 400 before 403/404 on the three array-bodied commissioner routes | this review, §2 | **Accepted.** The 400 comes from model binding and is identical whether or not the league exists, so it is not an existence oracle. Fixing it would mean moving the league filter into middleware ahead of binding - a large change for no leak. |

---

## 11. Findings summary

| # | Finding | Severity | Resolution |
|---|---|---|---|
| 1 | `ReturnUrl.Sanitize` accepted a control character mid-string, so a CR/LF reached the `Location` header | Low (Kestrel 500; response splitting in principle) | **Fixed**, commit `9f02035` |
| 2 | `GetAuditAsync` 500s the whole audit page on one oddly-shaped `Details` blob | Medium (availability, member-reachable) | **Fixed**, D-156, commit `a748ad5` |
| 3 | `ListNeedsVoidReviewAsync` lists removed rows that void/override then 404 | Low (dead action; P5-05 blocker) | **Fixed**, D-156, commit `a748ad5` |
| 4 | GamesAdded push re-fires on every later add | Medium (notification fatigue) | **Fixed**, D-155, commit `db64eb4` |
| 5 | Friday reminders fire all off-season | Low | **Fixed**, D-154, commit `db64eb4` |
| 6 | No rate limit on `/auth/*` or invite redemption | Medium | **Fixed**, D-153 |
| 7 | Endpoint filters invisible to any structural test, so "every route is scoped" was unverifiable | Process | **Fixed**, D-157 - marker metadata + inventory tests |
| 8 | `/api/admin/fixture/*` scoped `Authenticated`, not commissioner | Low | **Accepted** (Development/Testing only; §10) |
| 9 | 400 before 403/404 on three array-bodied routes | Informational | **Accepted** (§10) |
| 10 | ASP.NET packages one patch behind (10.0.11 vs 10.0.12) | Informational | **Deferred** to P8-02/P8-04 - must move with the installed runtime |

No finding was left both unresolved and unlogged.

---

## 12. New and changed tests

| File | New tests | Covers |
|---|---|---|
| `RouteInventoryTests.cs` (new) | 6 | §1 |
| `GeneratedAuthMatrixTests.cs` (new) | 5 | §2 |
| `CookieSecurityTests.cs` (new) | 4 | §3 |
| `ProductionBehaviourTests.cs` (new) | 2 | §3 |
| `RateLimitTests.cs` (new) | 3 | §5 |
| `NeverRulesTests.cs` (new) | 1 (five surfaces) | §4 |
| `CorrectionEdgeCaseTests.cs` (new) | 7 | §8 |
| `Infrastructure/RouteInventory.cs`, `Infrastructure/ApiContractRoutes.cs` (new) | - | test infrastructure |
| `ReturnUrlTests.cs` | +16 cases | §3 |
| `CsrfTests.cs` | +2 | §3 |
| `EventNotificationTests.cs` | +2 | §9 |
| `ReminderJobTests.cs` | +3 cases | §9 |

`dotnet test`: **821 passed, 0 failed** (306 Domain, 515 Api) on the branch alone; **822** after merging `main` @ 969c461 (P5-05, P8-03), which adds one simulation test and no endpoint.
