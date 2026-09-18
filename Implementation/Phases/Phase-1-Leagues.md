# Phase 1 - Leagues and Members

Stories: 01 Leagues and Members, 08 (display names), 13 (season range).
Depends on: Phase 0 complete. Runs in parallel with Phases 2 and 3a.

Read first: WorkItems 01, `02-Data-Model.md` (Leagues, Memberships, Invites), `03-API-Contracts.md` (Auth, Leagues, Season calendar), `04-Domain-Algorithms.md` section 1.

---

## P1-01 League and membership service and endpoints
Tier: Sonnet (Opus review). Depends on: P0-03, P0-05.

Deliverables
- `Domain/Leagues`: `League`, `Membership`, `Invite` entities; `LeagueRules` static checks: name 1..50, member cap 50, at-least-one-commissioner invariant, cannot remove self, transfer semantics (target promoted, caller demoted, other commissioners untouched).
- `Infrastructure/Services/LeagueService` and `InviteService`.
- Endpoints in `LeagueEndpoints.cs` and `InviteEndpoints.cs` exactly per contracts: create, list mine, get, settings, members, display-name, invites CRUD, invite preview, accept, remove, promote, demote, transfer.
- Create: `FirstWeek`/`LastWeek` default from `SeasonCalendar` and are validated to regular-season weeks; `DefaultPointValue = 10`.
- Accept invite: sets `JoinedWeek = CurrentWeek`; refuses when Full, Expired, Revoked, AlreadyMember with the contract's 409 + State.
- Audit entries for MemberRemoved, RolePromoted, RoleDemoted, RoleTransferred.

Done when
- `LeagueEndpointsTests`, `InviteAcceptTests` (valid, expired, revoked, full, already member), `RoleTests` (promote, demote, demote-last-commissioner 409, transfer, remove-self 409, remove-last-commissioner 409), auth matrix for both groups.

## P1-02 League UI
Tier: Sonnet. Depends on: P1-01 contracts (can start against contracts with mocked `HttpClient`; integrate when P1-01 merges).

Deliverables (Blazor pages under `Pages/Leagues/`)
- **League picker** (`/`): list of my leagues with role, current week, my status pill. "Create league" button. Empty state explains invites.
- **Create league** (`/leagues/new`): name, season year (default current), first/last week pickers limited to regular season.
- **League home** (`/leagues/{id}`): name, "Week N", my status, lock time in local zone with Eastern hint, big buttons to Picks, Dashboard, Leaderboard. Commissioner sees a "Members not submitted: N" line and a Manage link. Before first week: "Season starts Week N on <date>". After last week: "Season complete".
- **Members** (`/leagues/{id}/members`): roster with role badges; commissioners see per-member status and actions (promote, demote, remove, transfer) behind a confirm sheet.
- **Invites** (`/leagues/{id}/invites`): create, list, revoke, "Share" using `navigator.share` when available, else copy.
- **Join** (`/join/{code}`): preview card and Join button; handles every `State`.
- **Settings** (`/leagues/{id}/settings`): name, weeks, default point value (commissioner only).

Done when
- All pages render at 375px with no horizontal scroll; screenshots attached.
- Works end to end against P1-01 in Development with fixtures.

## P1-03 Display names
Tier: Sonnet. Depends on: P0-03, P1-01.

Deliverables
- `PUT /api/me` (global name) and `PUT /api/leagues/{id}/members/me/display-name` (per-league override) with uniqueness check against effective names in the league (409 with a friendly message).
- **Profile** page (`/me`): global display name, sign out, and a placeholder "Notifications" section for Phase 7.
- Every DTO that carries a member name uses the effective per-league name (`DisplayNameOverride ?? User.DisplayName`) via one shared projection helper.

Done when
- `DisplayNameTests`: length bounds, per-league uniqueness, projection helper used by members endpoint.

---

## Phase exit criteria
- A user can create a league, invite by link, a second user joins, roles can be promoted/demoted/transferred, and the league home shows the current week from `SeasonCalendar`.
- Auth matrix tests pass for all league routes.
