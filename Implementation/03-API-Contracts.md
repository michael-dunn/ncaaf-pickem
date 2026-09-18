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
| POST | `/auth/logout` | Auth | Signs out, clears cookie. |
| GET | `/api/me` | Auth | `MeResponse { UserId, Email, DisplayName, Leagues: LeagueSummary[] }` |
| PUT | `/api/me` | Auth | `UpdateMeRequest { DisplayName }` 1..30 chars. |

Unauthenticated `/api/*` = 401 (not a redirect; the SPA handles it).

## Leagues and members (Feature 01)

| Method | Route | Scope | Request / Response |
|---|---|---|---|
| POST | `/api/leagues` | Auth | `CreateLeagueRequest { Name, SeasonYear, FirstWeek?, LastWeek? }` -> `LeagueDetail` |
| GET | `/api/leagues` | Auth | `LeagueSummary[] { LeagueId, Name, SeasonYear, MyRole, CurrentWeek, MyCurrentWeekStatus }` |
| GET | `/api/leagues/{leagueId}` | Member | `LeagueDetail { LeagueId, Name, SeasonYear, FirstWeek, LastWeek, DefaultPointValue, CurrentWeek, IsComplete, MyRole, MyCurrentWeekStatus, CurrentWeekLockAtUtc?, LockAtEasternDisplay? }` |
| PUT | `/api/leagues/{leagueId}/settings` | Commish | `UpdateLeagueSettingsRequest { Name, FirstWeek, LastWeek, DefaultPointValue }` |
| GET | `/api/leagues/{leagueId}/members` | Member | `MemberRow[] { MembershipId, DisplayName, Role, JoinedWeek, IsFormer, CurrentWeekStatus? }` (status only populated for Commish callers) |
| PUT | `/api/leagues/{leagueId}/members/me/display-name` | Member | `SetLeagueDisplayNameRequest { DisplayName }`; 409 if taken in league |
| POST | `/api/leagues/{leagueId}/invites` | Commish | -> `InviteResponse { Code, Url, ExpiresUtc }` |
| GET | `/api/leagues/{leagueId}/invites` | Commish | `InviteResponse[]` (active only) |
| DELETE | `/api/leagues/{leagueId}/invites/{inviteId}` | Commish | revoke |
| GET | `/api/invites/{code}` | Auth | `InvitePreview { LeagueName, SeasonYear, MemberCount, State: Valid/Expired/Revoked/Full/AlreadyMember }` |
| POST | `/api/invites/{code}/accept` | Auth | -> `LeagueDetail`; 409 with State on any non-Valid state |
| DELETE | `/api/leagues/{leagueId}/members/{membershipId}` | Commish | soft remove; 409 if target is self or would leave zero commissioners |
| POST | `/api/leagues/{leagueId}/members/{membershipId}/promote` | Commish | -> Commissioner |
| POST | `/api/leagues/{leagueId}/members/{membershipId}/demote` | Commish | 409 if last commissioner |
| POST | `/api/leagues/{leagueId}/commissioner/transfer` | Commish | `TransferRequest { ToMembershipId }`; promotes target, demotes caller |

## Season calendar (Feature 13)

| Method | Route | Scope | Response |
|---|---|---|---|
| GET | `/api/seasons/{year}/weeks` | Auth | `SeasonWeek[] { Week, StartUtc, EndUtc, IsRegularSeason }` |
| GET | `/api/leagues/{leagueId}/weeks` | Member | `LeagueWeek[] { Week, HasGameSet, IsCurrent, IsLocked, IsComplete, LockAtUtc? }` restricted to First..Last |

## Game set configuration (Feature 02)

| Method | Route | Scope | Request / Response |
|---|---|---|---|
| GET | `/api/leagues/{leagueId}/gameset-rules` | Commish | `GameSetRuleDto[] { RuleId, RuleType, ConferenceId?, ConferenceName?, TeamId?, TeamName?, ConferenceGamesOnly, SortOrder }` (default config) |
| PUT | `/api/leagues/{leagueId}/gameset-rules` | Commish | `GameSetRuleDto[]` full replace |
| GET | `/api/leagues/{leagueId}/weeks/{week}/gameset-rules` | Commish | `WeekRulesResponse { UsesOverride, Rules: GameSetRuleDto[] }` |
| PUT | `/api/leagues/{leagueId}/weeks/{week}/gameset-rules` | Commish | `WeekRulesResponse`; `UsesOverride=false` clears override |
| POST | `/api/leagues/{leagueId}/weeks/{week}/gameset/preview` | Commish | `GameSetRuleDto[]` (candidate rules) -> `GameSetPreview { Games: GameSetGameDto[], Count, ExceedsMax }` |
| POST | `/api/leagues/{leagueId}/weeks/{week}/gameset/generate` | Commish | regenerates from saved rules; 409 if locked or Count > 50 -> `WeekGameSetResponse` |
| POST | `/api/leagues/{leagueId}/weeks/{week}/gameset/games` | Commish | `AddGameRequest { GameId }` manual add; 409 if locked, not Saturday, non-FBS, or would exceed 50 |
| DELETE | `/api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}` | Commish | manual remove; 409 if locked |
| GET | `/api/leagues/{leagueId}/weeks/{week}/gameset` | Member | `WeekGameSetResponse { Week, LockAtUtc?, LockAtEasternDisplay?, IsLocked, IsComplete, Games: GameSetGameDto[] }` ordered by kickoff |
| GET | `/api/seasons/{year}/weeks/{week}/games?search=` | Commish | `GameCandidate[]` Saturday FBS games for manual add |
| GET | `/api/reference/conferences` | Auth | `ConferenceDto[]` FBS only |
| GET | `/api/reference/teams?search=` | Auth | `TeamDto[]` FBS only |

`GameSetGameDto { GameSetGameId, GameId, HomeTeam: TeamDto, AwayTeam: TeamDto, HomeRank?, AwayRank?, KickoffUtc, PointValue, IsPointValueElevated, Source, Status, HomeScore?, AwayScore?, Period?, Clock?, IsVoided, WinnerTeamId? }`

## Point values (Feature 03)

| Method | Route | Scope | Request / Response |
|---|---|---|---|
| GET | `/api/leagues/{leagueId}/point-rules` | Commish | `PointRuleDto[] { RuleId, Priority, RuleType, ConferenceId?, TeamId?, SpreadThreshold?, PointValue }` |
| PUT | `/api/leagues/{leagueId}/point-rules` | Commish | full replace, order = priority; re-resolves all unlocked weeks |
| PUT | `/api/leagues/{leagueId}/weeks/{week}/gameset/games/{gameId}/points` | Commish | `SetPointOverrideRequest { PointValue? }` null clears; 409 if locked |

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
| GET | `/api/admin/data-status` | Commish (any league) | `DataStatusResponse { Refreshes: { DataType, LastSuccessUtc?, LastAttemptUtc?, LastError? }[], CfbdCallsThisMonth, CfbdWarning (>= 800), LiveScoreSource, Unmatched: UnmatchedGameDto[], RecentJobs: JobRunDto[] }` |
| POST | `/api/admin/refresh/{dataType}` | Commish | dataType in Teams, Schedule, Rankings, Lines, Scores; runs now; audit logged |
| POST | `/api/admin/unmatched/{id}/resolve` | Commish | `ResolveUnmatchedRequest { GameId }` creates TeamAliases |

## Push (Feature 11)

| Method | Route | Scope | Request / Response |
|---|---|---|---|
| GET | `/api/push/vapid-public-key` | Auth | `{ PublicKey }` |
| POST | `/api/push/subscriptions` | Auth | `PushSubscriptionRequest { Endpoint, P256dh, Auth, UserAgent }` upsert |
| DELETE | `/api/push/subscriptions` | Auth | `{ Endpoint }` |
| GET | `/api/push/status` | Auth | `{ HasSubscriptionForThisDevice }` (client passes `?endpoint=`) |
| GET | `/api/leagues/{leagueId}/notifications/log` | Commish | `NotificationLogRow[]` last 200 |

## Health

| GET | `/health` | Anon | liveness; `/health/ready` checks DB. |
