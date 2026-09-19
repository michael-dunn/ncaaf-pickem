# 03 - API Contracts

Frontend and backend agents build against this file. DTO names are the record names in `NcaafPickEm.Shared/Contracts/<Feature>/`. If a contract must change, the agent changing it updates this file in the same commit and messages the owner of the other side (see `06-Agent-Protocol.md`).

## Conventions

- Base path `/api`. Route params: `{leagueId:guid}`, `{week:int}`, `{gameId:guid}` (the `Games.Id`, not provider IDs), `{membershipId:guid}`.
- Auth scopes: **Anon**, **Auth** (signed in), **Member** (active membership in `leagueId`), **Commish** (membership with Role = Commissioner). Any commissioner of any league may read `/api/admin/*`.
- Mutations (`POST`, `PUT`, `DELETE`) require header `X-Requested-With: NcaafPickEm`. Missing header = 400.
- Errors use `ProblemDetails`. Validation errors = 400 with `errors` dictionary. Non-member access to a league = 404 (do not reveal existence). Commissioner-only by a member = 403.
- Times in DTOs are `DateTimeOffset` in UTC. The client renders local time. Lock time DTOs also include `LockAtEasternDisplay` string for the "Eastern lock" hint (Feature 13).
- All list responses are plain arrays; no paging needed at this scale.

## Auth (Feature 08)

| Method | Route | Scope | Notes |
|---|---|---|---|
| GET | `/auth/login/google?returnUrl=` | Anon | Challenges Google. `returnUrl` must be a relative path. |
| GET | `/auth/callback/google` | Anon | Handled by the Google middleware; upserts `Users`, signs in cookie, redirects to `returnUrl`. |
| POST | `/auth/logout` | Auth | Signs out, clears cookie. Returns 204. |
| GET | `/api/me` | Auth | `MeResponse { UserId, Email, DisplayName, Leagues: LeagueSummary[] }` |
| PUT | `/api/me` | Auth | `UpdateMeRequest { DisplayName }` 1..30 chars after trimming; 400 otherwise. 409 (P1-03) if the new name collides with another active member's effective name in a league where the caller has no per-league override (D-058). |

Unauthenticated `/api/*` = 401 (not a redirect; the SPA handles it).

`/auth/callback/google` has no endpoint of its own: it is the Google handler's `CallbackPath`, answered by the authentication middleware. `returnUrl` on `/auth/login/google` must be a relative path; anything else (absolute, protocol-relative, or a bare `javascript:`) silently falls back to `/` rather than failing the login.

`/auth/*` is not covered by the CSRF header rule — the whole `/api` group is. `POST /auth/logout` is protected instead by the cookie being `SameSite=Lax`, which a cross-site form post never carries.

`MeResponse.Leagues` is `[]` until **P1-01** wires up league summaries; `LeagueSummary` is already defined in `Shared/Contracts/Leagues` with `MyCurrentWeekStatus` nullable (null when the current week has no game set yet).

## Leagues and members (Feature 01)

| Method | Route | Scope | Request / Response |
|---|---|---|---|
| POST | `/api/leagues` | Auth | `CreateLeagueRequest { Name, SeasonYear, FirstWeek?, LastWeek? }` -> `LeagueDetail` |
| GET | `/api/leagues` | Auth | `LeagueSummary[] { LeagueId, Name, SeasonYear, MyRole, CurrentWeek, MyCurrentWeekStatus }` |
| GET | `/api/leagues/{leagueId}` | Member | `LeagueDetail { LeagueId, Name, SeasonYear, FirstWeek, LastWeek, DefaultPointValue, CurrentWeek, IsComplete, MyRole, MyCurrentWeekStatus, CurrentWeekLockAtUtc?, LockAtEasternDisplay? }` |
| PUT | `/api/leagues/{leagueId}/settings` | Commish | `UpdateLeagueSettingsRequest { Name, FirstWeek, LastWeek, DefaultPointValue }` |
| GET | `/api/leagues/{leagueId}/members` | Member | `MemberRow[] { MembershipId, DisplayName, Role, JoinedWeek, IsFormer, CurrentWeekStatus?, IsMe }` (status only populated for Commish callers; `IsMe` added P1-03, additive) |
| PUT | `/api/leagues/{leagueId}/members/me/display-name` | Member | `SetLeagueDisplayNameRequest { DisplayName }` -> caller's own updated `MemberRow` (`IsMe = true`); 409 if taken by another **active** member (case-insensitive; a removed member's old name is free, D-039) |
| POST | `/api/leagues/{leagueId}/invites` | Commish | -> `InviteResponse { Code, Url, ExpiresUtc }` |
| GET | `/api/leagues/{leagueId}/invites` | Commish | `InviteResponse[]` (active only) |
| DELETE | `/api/leagues/{leagueId}/invites/{inviteId}` | Commish | revoke |
| GET | `/api/invites/{code}` | Auth | `InvitePreview { LeagueName, SeasonYear, MemberCount, State: Valid/Expired/Revoked/Full/AlreadyMember }`; 404 for an unknown code |
| POST | `/api/invites/{code}/accept` | Auth | -> `LeagueDetail`; 409 with State on any non-Valid state. When several states apply, priority is Revoked > Expired > AlreadyMember > Full (D-038). A caller who was previously a member and was removed reactivates their existing membership row rather than getting a second one (D-037). An unknown code (`GET`/`POST /api/invites/{code}*`) is 404. |
| DELETE | `/api/leagues/{leagueId}/members/{membershipId}` | Commish | soft remove; 404 if `membershipId` is unknown/not-in-league (P1-03, was 400); 409 if target is self or would leave zero commissioners |
| POST | `/api/leagues/{leagueId}/members/{membershipId}/promote` | Commish | -> Commissioner; 404 if `membershipId` is unknown/not-in-league |
| POST | `/api/leagues/{leagueId}/members/{membershipId}/demote` | Commish | 404 if `membershipId` is unknown/not-in-league; 409 if last commissioner |
| POST | `/api/leagues/{leagueId}/commissioner/transfer` | Commish | `TransferRequest { ToMembershipId }`; promotes target, demotes caller; 404 if `ToMembershipId` is unknown/not-in-league |

## Season calendar (Feature 13)

| Method | Route | Scope | Response |
|---|---|---|---|
| GET | `/api/seasons/{year}/weeks` | Auth | `SeasonWeek[] { Week, StartUtc, EndUtc, IsRegularSeason }`; 404 when the calendar has no such season (P0-05). Weeks are ordered ascending and `Week` may be 0. |
| GET | `/api/leagues/{leagueId}/weeks` | Member | `LeagueWeek[] { Week, HasGameSet, IsCurrent, IsLocked, IsComplete, LockAtUtc? }` restricted to First..Last |

## Game set configuration (Feature 02)

| Method | Route | Scope | Request / Response |
|---|---|---|---|
| GET | `/api/leagues/{leagueId}/gameset-rules` | Commish | `GameSetRuleDto[] { RuleId, RuleType, ConferenceId?, ConferenceName?, TeamId?, TeamName?, ConferenceGamesOnly, SortOrder }` (default config) |
| PUT | `/api/leagues/{leagueId}/gameset-rules` | Commish | `GameSetRuleDto[]` full replace; **save only** (does not regenerate any week) |
| GET | `/api/leagues/{leagueId}/weeks/{week}/gameset-rules` | Commish | `WeekRulesResponse { UsesOverride, Rules: GameSetRuleDto[] }` |
| PUT | `/api/leagues/{leagueId}/weeks/{week}/gameset-rules` | Commish | `WeekRulesResponse`; `UsesOverride=false` clears override; **save only**; 409 if the week is already locked |
| POST | `/api/leagues/{leagueId}/weeks/{week}/gameset/preview` | Commish | `GameSetRuleDto[]` (candidate rules) -> `GameSetPreview { Games: GameSetGameDto[], Count, ExceedsMax }` |
| POST | `/api/leagues/{leagueId}/weeks/{week}/gameset/generate` | Commish | regenerates from saved rules; 409 if locked or Count > 50 -> `WeekGameSetResponse` |
| POST | `/api/leagues/{leagueId}/weeks/{week}/gameset/games` | Commish | `AddGameRequest { GameId }` manual add; 409 if locked, not Saturday, non-FBS, or would exceed 50 -> `WeekGameSetResponse` |
| DELETE | `/api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}` | Commish | manual remove; 409 if locked -> `WeekGameSetResponse` |
| GET | `/api/leagues/{leagueId}/weeks/{week}/gameset` | Member | `WeekGameSetResponse { Week, LockAtUtc?, LockAtEasternDisplay?, IsLocked, IsComplete, Games: GameSetGameDto[] }` ordered by kickoff |
| GET | `/api/seasons/{year}/weeks/{week}/games?search=` | Commish (any league) | `GameCandidate[]` Saturday FBS games for manual add |
| GET | `/api/reference/conferences` | Auth | `ConferenceDto[]` FBS only |
| GET | `/api/reference/teams?search=` | Auth | `TeamDto[]` FBS only |

`GameSetGameDto { GameSetGameId, GameId, HomeTeam: TeamDto, AwayTeam: TeamDto, HomeRank?, AwayRank?, KickoffUtc, PointValue, IsPointValueElevated, Source, Status, HomeScore?, AwayScore?, Period?, Clock?, IsVoided, WinnerTeamId? }`

`TeamDto { TeamId, School, Abbreviation?, ConferenceId?, LogoUrl? }`, `ConferenceDto { ConferenceId, Name, Abbreviation }`, `GameCandidate { GameId, HomeTeam, AwayTeam, HomeRank?, AwayRank?, KickoffUtc, IsConferenceGame }`. `GameSetPreview` also carries `UsedFallbackRankings`. `GameSetGameId` is null in previews. Record definitions live in `Shared/Contracts/{GameSets,Points,Reference}`.

**P3-03 clarifications** (D-053/D-063, D-054/D-064, D-055/D-065):
- Both `PUT .../gameset-rules` routes (default and week-override) **save only**; they never call `generate` themselves. The commissioner UI's "Save and generate" action is two calls: `PUT` then `POST .../generate`. The Tuesday auto-regeneration job (P3-04) is the other caller of `generate`.
- `POST .../gameset/games` and `DELETE .../gameset/games/{gameId}` both return the full `WeekGameSetResponse` (same shape as `generate`/`GET .../gameset`), not a bare `GameSetGameDto` or `204`, so the caller does not need a second round trip to see the updated `LockAtUtc` or games list.
- Any route carrying `{week}` 404s (`ProblemDetails` title `WeekOutOfRange`) when the week is outside the league's `FirstWeek..LastWeek` range, checked before anything else.
- A 409 for `Locked` or `GameNotEligible` carries no extra body; a 409 for `ExceedsMax` (on `generate` or the manual-add cap) adds `"count"` to the `ProblemDetails` extensions with how many games the configuration would produce (or the set would hold after the add).

## Point values (Feature 03)

| Method | Route | Scope | Request / Response |
|---|---|---|---|
| GET | `/api/leagues/{leagueId}/point-rules` | Commish | `PointRuleDto[] { RuleId, Priority, RuleType, ConferenceId?, TeamId?, SpreadThreshold?, PointValue, ConferenceName?, TeamName? }` |
| PUT | `/api/leagues/{leagueId}/point-rules` | Commish | full replace; `Priority` is taken from each entry, not from array order (must be unique — validated); re-resolves all unlocked weeks immediately |
| PUT | `/api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/points` | Commish | `SetPointOverrideRequest { PointValue? }` null clears; 409 if locked; re-resolves immediately -> `GameSetGameDto` |

`ConferenceName`/`TeamName` (D-068) are read-only display echoes, added trailing/additive to `PointRuleDto` at P3-05's request while building the config UI in parallel — matches `GameSetRuleDto`'s existing echo fields.

## Picks (Feature 04)

| Method | Route | Scope | Request / Response |
|---|---|---|---|
| GET | `/api/leagues/{leagueId}/weeks/{week}/picks/me` | Member | `MyPicksResponse { Week, Status, LockAtUtc?, IsLocked, PickedCount, TotalCount, HasUnseenGameChanges, Games: MyPickGameDto[] }` |
| PUT | `/api/leagues/{leagueId}/weeks/{week}/picks/me/{gameId}` | Member | `SetPickRequest { TeamId }` -> `MyPicksResponse`; 409 if locked; 400 if team not in game |
| POST | `/api/leagues/{leagueId}/weeks/{week}/picks/me/submit` | Member | 409 if any active game unpicked or locked -> `MyPicksResponse` |
| POST | `/api/leagues/{leagueId}/weeks/{week}/picks/me/ack-changes` | Member | clears `HasUnseenGameChanges` |
| GET | `/api/leagues/{leagueId}/weeks/{week}/picks` | Member | all members' picks; **403 before lock**. `WeekPicksResponse { Games: GameSetGameDto[], Members: MemberPicksRow[] { MembershipId, DisplayName, Status, Picks: { GameSetGameId, TeamId? }[] } }` |
| GET | `/api/leagues/{leagueId}/weeks/{week}/picks/status` | Commish | `MemberStatusRow[] { MembershipId, DisplayName, Status, PickedCount, TotalCount }` |

`MyPickGameDto` = `GameSetGameDto` + `{ MyTeamId?, IsNewSinceSubmit }`.

## Influence dashboard (Feature 05)

| Method | Route | Scope | Response |
|---|---|---|---|
| GET | `/api/leagues/{leagueId}/weeks/{week}/dashboard` | Member | Before lock: `DashboardResponse { IsAvailable=false, LockAtUtc }`. After: `{ IsAvailable=true, PointsSoFar, MaxRemaining, ScoresMayBeStale, Games: DashboardGameDto[], EveryoneAgrees: DashboardGameDto[] }` |

`DashboardGameDto { Game: GameSetGameDto, MyTeamId?, MyOutcome: Pending/Won/Lost/NoPick, OppositeCount, OppositePicks: MemberRef[], NoPick: MemberRef[], SwingPoints }`. Ordering rules in `04-Domain-Algorithms.md` section 6. Client polls every 60 s while any game is not Final.

## Scoring and corrections (Feature 06)

| Method | Route | Scope | Request / Response |
|---|---|---|---|
| POST | `/api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/override-result` | Commish | `OverrideResultRequest { WinnerTeamId, Reason }`; only after lock |
| POST | `/api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/void` | Commish | `VoidGameRequest { Reason }`; only after lock |
| GET | `/api/leagues/{leagueId}/audit` | Member | `AuditEntry[] { CreatedUtc, ActorName, Action, Summary }` newest first |

## Leaderboard (Feature 07)

| Method | Route | Scope | Response |
|---|---|---|---|
| GET | `/api/leagues/{leagueId}/leaderboard` | Member | `SeasonLeaderboard { ThroughWeek, Rows: SeasonRow[] { Rank, MembershipId, DisplayName, TotalPoints, PointsBehind, WeeklyWins, Trend: Up/Down/Same/None, IsMe } }` |
| GET | `/api/leagues/{leagueId}/weeks/{week}/leaderboard` | Member | `WeekLeaderboard { Week, IsComplete, Rows: WeekRow[] { Rank, MembershipId, DisplayName, Points, Correct, Total, IsWinner, IsFormer, IsMe } }` |
| GET | `/api/leagues/{leagueId}/weeks/{week}/grid` | Member | `WeekGrid { Games: GameSetGameDto[], Members: GridMember[] { MembershipId, DisplayName, IsFormer }, Cells: GridCell[] { GameSetGameId, MembershipId, TeamId?, Outcome: Pending/Correct/Incorrect/NoPick/Voided } }`; 403 before lock |

## Data admin (Features 09, 12)

| Method | Route | Scope | Response |
|---|---|---|---|
| GET | `/api/admin/data-status` | Commish (any league) | `DataStatusResponse { Refreshes: { DataType, LastSuccessUtc?, LastAttemptUtc?, LastError? }[], CfbdCallsThisMonth, CfbdWarning (>= 800), LiveScoreSource (configured), ActiveLiveScoreSource, ScoresMayBeStale, Unmatched: UnmatchedGameDto[], RecentJobs: JobRunDto[], NeedsReview: NeedsReviewGameDto[] { GameId, LeagueId, LeagueName, Week, HomeTeam, AwayTeam, HomeScore?, AwayScore?, Reason } }` (P2-04 additive: `ActiveLiveScoreSource`, `ScoresMayBeStale`, `NeedsReview`; `LiveScoreSource` keeps its P0-06 meaning, the *configured* provider — see D-080) |
| POST | `/api/admin/refresh/{dataType}` | Commish | dataType in Teams, Schedule, Rankings, Lines, Scores; runs synchronously against the current season/week (Scores runs one live-score poll for today's Eastern date regardless of the Saturday window); 202 `ManualRefreshResponse { DataType, Success, Error? }`; audit logged as `ManualRefresh` against the caller's first commissioner league (D-079); 400 for an unrecognized dataType |
| POST | `/api/admin/unmatched/{id}/resolve` | Commish | `ResolveUnmatchedRequest { GameId }`; creates `TeamAliases(Source = the unmatched row's own Source)` for the raw home/away names, learns `Games.EspnEventId` from the raw payload when the source is Espn and it is not already set, marks `ResolvedUtc`; 204; 404 for an unknown unmatched id; 400 for an unknown or empty `GameId`; 409 when a raw name is already an alias of a *different* team (D-082) |

## Push (Feature 11)

| Method | Route | Scope | Request / Response |
|---|---|---|---|
| GET | `/api/push/vapid-public-key` | Auth | `VapidPublicKeyResponse { PublicKey }`, or **503** `ProblemDetails` when `Push__*` is unset or invalid (D-073) |
| POST | `/api/push/subscriptions` | Auth | `PushSubscriptionRequest { Endpoint, P256dh, Auth, UserAgent? }` upsert by `Endpoint` -> **204** |
| DELETE | `/api/push/subscriptions` | Auth | body `DeletePushSubscriptionRequest { Endpoint }` -> **204**, idempotent |
| GET | `/api/push/status?endpoint=` | Auth | `PushStatusResponse { HasSubscriptionForThisDevice }` |
| POST | `/api/push/test` | Commish (any league) | **Development/Testing only.** Sends "Test notification" to the caller's own devices; returns the `NotificationResult`. |
| GET | `/api/leagues/{leagueId}/notifications/log` | Commish | `NotificationLogRow[] { CreatedUtc, UserDisplayName, Week, Type, Result, Error }` last 200, newest first |

DTOs live in `NcaafPickEm.Shared/Contracts/Push/`. Notes P7-02 needs:

- `POST /subscriptions` is an **upsert keyed on `Endpoint`**: posting the same endpoint twice leaves one row, and posting an endpoint another account owns re-owns it (D-075). `Endpoint` must be an absolute https URL of at most 2048 characters and both keys must be non-empty, or it is 400 with an `errors` dictionary.
- `DELETE /subscriptions` only removes the caller's own subscription; an unknown endpoint, or one owned by somebody else, is still 204.
- `GET /status` with no `endpoint` query value answers `false` rather than 400.
- The 503 from `vapid-public-key` is the "notifications unavailable on this server" signal; the settings page should hide the turn-on action rather than retry.

## Health

| GET | `/health` | Anon | liveness; `/health/ready` checks DB. |
