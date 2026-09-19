# 02 - Data Model

All IDs are `uniqueidentifier` (Guid v7 generated in code) unless noted. All timestamps are `datetime2` in UTC with a `Utc` suffix. Soft-delete only where the stories require history (memberships, game-set games). Schema owner: Phase 0 (P0-02) creates the initial migration; later phases add migrations named `Phase<N>_<Task>_<What>`.

## Identity (Feature 08)

**Users**

| Column | Type | Notes |
|---|---|---|
| Id | Guid PK | |
| GoogleSubject | nvarchar(64) UQ | Match key on returning login. |
| Email | nvarchar(256) UQ | |
| DisplayName | nvarchar(30) | Initial value = Google name, trimmed to 30. |
| CreatedUtc, LastLoginUtc | datetime2 | |

## Leagues (Features 01, 13)

**Leagues**

| Column | Type | Notes |
|---|---|---|
| Id | Guid PK | |
| Name | nvarchar(50) | non-empty |
| SeasonYear | int | One league = one season. |
| FirstWeek, LastWeek | int | Defaults: 1 and final regular-season week from `SeasonWeeks`. |
| DefaultPointValue | int | 1..100, default 10. |
| CreatedByUserId | Guid FK Users | |
| CreatedUtc | datetime2 | |
| IsComplete | bit | Set by job when now is past the LastWeek window end. |

**Memberships**

| Column | Type | Notes |
|---|---|---|
| Id | Guid PK | |
| LeagueId FK, UserId FK | | UQ(LeagueId, UserId) |
| Role | tinyint | 0 Member, 1 Commissioner. Multiple commissioners allowed (Feature 01). |
| DisplayNameOverride | nvarchar(30) null | Per-league name; uniqueness of effective name within league enforced in service. |
| JoinedUtc | datetime2 | |
| JoinedWeek | int | Week current at join. Mid-season joiners have no participation before this week. |
| RemovedUtc | datetime2 null | Soft delete. Former members keep picks for history. |

Invariant: a league always has at least one active Commissioner. Demote, remove, and transfer must check this.

**Invites**

| Column | Type | Notes |
|---|---|---|
| Id | Guid PK | |
| LeagueId FK | | |
| Code | nvarchar(12) UQ | URL-safe, shareable by text. |
| CreatedByMembershipId FK | | |
| ExpiresUtc | datetime2 | default +14 days |
| RevokedUtc | datetime2 null | |
| MaxUses, Uses | int | default 50 / 0 |

## Season reference data (Features 09, 13)

**SeasonWeeks** (from provider calendar)

| Column | Type | Notes |
|---|---|---|
| SeasonYear, Week | int, int PK | Week 0 allowed. |
| StartUtc, EndUtc | datetime2 | Sunday 00:00 ET to Saturday 23:59:59 ET, converted to UTC. |
| IsRegularSeason | bit | Championship week and later = false; out of scope. |

**Conferences**: Id, CfbdId int UQ, Name, Abbreviation, Classification (FBS/FCS), EspnGroupId int null.

**Teams**

| Column | Notes |
|---|---|
| Id Guid PK | |
| CfbdId int UQ | |
| School, Mascot, Abbreviation | |
| ConferenceId FK null | |
| Classification | FBS / FCS / Other, from CFBD. Only FBS eligible (Feature 02). |
| LogoUrl | provider URL, not stored locally |
| EspnTeamId int null | filled by matching (P2-03) |

**TeamAliases**: TeamId FK, Source (Espn/Cfbd), Alias nvarchar(100). UQ(Source, Alias). Manual + learned name mappings.

**Games**

| Column | Notes |
|---|---|
| Id Guid PK | |
| CfbdGameId long UQ | source of truth |
| EspnEventId long null UQ | matched |
| SeasonYear, Week | int | provider week |
| HomeTeamId, AwayTeamId FK | |
| KickoffUtc | datetime2 | |
| KickoffEasternDate | date | computed at ingest |
| IsSaturdayEastern | bit | computed at ingest |
| IsConferenceGame | bit | from CFBD |
| Status | tinyint | Scheduled, InProgress, Final, Postponed, Cancelled |
| HomeScore, AwayScore | int null | |
| Period, Clock | tinyint null, nvarchar(8) null | live |
| Venue | nvarchar(100) null | |
| LastScoreUpdateUtc | datetime2 null | |

**GameLines**: GameId FK, Provider nvarchar(40), Spread decimal(5,1) (home minus away; negative = home favored), FetchedUtc. Keep history; "current" = latest FetchedUtc.

**Rankings**: SeasonYear, Week, Poll ('AP'), Rank int, TeamId FK, FetchedUtc. PK(SeasonYear, Week, Poll, Rank).

## League configuration (Features 02, 03)

**GameSetRules**

| Column | Notes |
|---|---|
| Id Guid PK | |
| LeagueId FK | |
| Week int null | null = default configuration; non-null = week override |
| RuleType | tinyint: Top25, Conference, Team |
| ConferenceId FK null, TeamId FK null | per type |
| ConferenceGamesOnly | bit | Conference type only |
| SortOrder | int | display only; rules are unioned |

When `WeekGameSets.UsesOverride = 1` for a week, only rows with that Week are used; otherwise only rows with Week null.

**PointRules**

| Column | Notes |
|---|---|
| Id, LeagueId FK | |
| Priority int | lower = higher priority; top of list wins |
| RuleType | tinyint: ConferenceGame, CloseSpread, Team |
| ConferenceId null, TeamId null, SpreadThreshold decimal null | |
| PointValue int | 1..100 |

**WeekGameSets**

| Column | Notes |
|---|---|
| Id Guid PK | |
| LeagueId FK, Week int | UQ |
| UsesOverride | bit | |
| GeneratedUtc | datetime2 | |
| LockAtUtc | datetime2 null | earliest Saturday kickoff among active games; null if none |
| LockedUtc | datetime2 null | set by lock job; frozen after |
| IsComplete | bit | all active games Final or Voided |

**WeekGameSetGames**

| Column | Notes |
|---|---|
| Id Guid PK | |
| WeekGameSetId FK, GameId FK | UQ |
| Source | tinyint: Rule, Manual |
| IsRemoved | bit | manual removal or schedule change before lock; stays removed on regen |
| RemovedReason | nvarchar(200) null | |
| PointValueOverride | int null | commissioner manual value |
| ResolvedPointValue | int | computed on generate/edit; frozen at lock |
| SpreadAtLock | decimal null | snapshot |
| IsVoided | bit | after lock only (Feature 06) |
| ResultOverrideWinnerTeamId | Guid null | commissioner correction |

"Active game" = `IsRemoved = 0 AND IsVoided = 0`.

## Picks (Feature 04)

**Picks**

| Column | Notes |
|---|---|
| Id Guid PK | |
| MembershipId FK, WeekGameSetGameId FK | UQ |
| PickedTeamId FK | must be home or away of that game |
| UpdatedUtc | |

**WeekSubmissions**

| Column | Notes |
|---|---|
| MembershipId FK, WeekGameSetId FK | PK |
| Status | tinyint: NotStarted, InProgress, Submitted, Locked, Incomplete |
| SubmittedUtc null, LastChangedUtc | |
| HasUnseenGameChanges | bit | drives "new games highlighted" in UI |

Status derivation rules are in `04-Domain-Algorithms.md` section 4. `Locked` and `Incomplete` are written by the lock job; the others are recomputed on every pick change.

## Scoring (Features 06, 07)

**WeekResults** (materialized, idempotently recomputed)

| Column | Notes |
|---|---|
| MembershipId FK, WeekGameSetId FK | PK |
| Points int, CorrectCount int, ActiveGameCount int | |
| IsWeekComplete | bit | copy of set flag at compute time |
| ComputedUtc | |

**SeasonStandingsSnapshots** (for trend arrows): LeagueId, ThroughWeek, MembershipId PK; Rank int, TotalPoints int. Written when a week becomes Complete.

**AuditLog**: Id, LeagueId, ActorMembershipId, Action nvarchar(40) (ResultOverride, GameVoided, GameManuallyAdded, GameManuallyRemoved, MemberRemoved, RolePromoted, RoleDemoted, RoleTransferred, ManualRefresh), TargetId Guid null, Details nvarchar(max) JSON, CreatedUtc. Visible to all members per Feature 06.

## Notifications (Feature 11)

**PushSubscriptions**: Id, UserId FK, Endpoint nvarchar(2048), EndpointHash binary(32) UQ, P256dh, Auth, UserAgent, CreatedUtc, LastSuccessUtc, FailureCount.

`EndpointHash` is a persisted computed column, `CONVERT(binary(32), HASHBYTES('SHA2_256', [Endpoint]))`. It carries the unique index because SQL Server caps an index key at 1700 bytes and `nvarchar(2048)` is 4096 (D-017). Write `Endpoint`; look a device up by hash.

**NotificationLog**: Id, UserId, LeagueId, Week, Type tinyint (FridayReminder, CommissionerSummary, SaturdayReminder, GamesAdded, GameRemoved), SubscriptionId null, Result (Sent, Failed, Expired, Skipped), Error nvarchar(500) null, CreatedUtc. Filtered unique index on (UserId, LeagueId, Week, Type) WHERE Type IN (FridayReminder, CommissionerSummary, SaturdayReminder) enforces once per week.

One row per **notification**, not per device (D-066): that index permits exactly one reminder row per member, league and week, so a member with two devices still has one row. `Result` is the best outcome across their devices (Sent > Failed > Expired > Skipped), and `SubscriptionId` names the device that took delivery, or is null when none did. The row is written before anything is sent, so a duplicate-key violation *is* the once-per-week check.

**PushRetries** (P7-01, D-065): Id, SubscriptionId FK, NotificationLogId FK, PayloadJson nvarchar(1000), TtlSeconds int, Attempt int, NextAttemptUtc, CreatedUtc. One row per outstanding retry; `PushRetryJob` (an `IOneShotJob`) drains it and deletes the row as soon as the send succeeds, the subscription turns out to be gone, or the third attempt fails. Backoff is +1, +5, +15 minutes from `CreatedUtc`. Unique index on (NotificationLogId, SubscriptionId) so one notification queues at most one retry per device; index on NextAttemptUtc for the due query. Migration `Phase7_01_PushRetries`.

## Operations (Features 09, 12)

**ProviderCalls**: Id, Provider, Operation, StartedUtc, DurationMs, Success bit, StatusCode int null, Error nvarchar(500) null. Monthly CFBD count = COUNT(*) WHERE Provider = 'Cfbd' AND StartedUtc in month.

**DataRefreshStatus**: DataType PK (Teams, Schedule, Rankings, Lines, Scores), LastSuccessUtc, LastAttemptUtc, LastError nvarchar(1000) null.

**UnmatchedGames**: Id, Source, RawHomeName, RawAwayName, GameDate, RawPayload nvarchar(max), FirstSeenUtc, ResolvedUtc null. Shown on the data status page.

**JobRuns**: Id, JobName, ScheduledForUtc, StartedUtc, FinishedUtc, Success, Error. UQ(JobName, ScheduledForUtc) makes cron jobs idempotent across restarts.

## Indexes worth calling out

- `Games (SeasonYear, Week, IsSaturdayEastern)` for game set generation.
- `Games (Status)` filtered WHERE Status IN (Scheduled, InProgress) for the poller.
- `Picks (WeekGameSetGameId)` for dashboard and grid.
- `WeekResults (WeekGameSetId)` and `Memberships (LeagueId, RemovedUtc)` for leaderboards.
- `NotificationLog` filtered unique index as above, named `UX_NotificationLog_WeeklyReminderOncePerWeek`.
- `PushRetries (NextAttemptUtc)` for the one-shot job's "what is due?" query, and a unique `(NotificationLogId, SubscriptionId)`.
- `Games (EspnEventId)` unique, filtered `WHERE [EspnEventId] IS NOT NULL` — the column is null until the matcher runs, and SQL Server would otherwise allow only one unmatched game in the table.

## How this is implemented (P0-02)

- Entities are plain classes in `src/NcaafPickEm.Domain/<Feature>/` with no EF attributes; all mapping is Fluent, one file per table in `src/NcaafPickEm.Infrastructure/Data/Configurations/<Entity>Configuration.cs`. The initial migration is `Phase0_02_InitialSchema`.
- Two entity names differ from the table name to leave the DTO name free: `AuditLogEntry` maps to `AuditLog`, `NotificationLogEntry` maps to `NotificationLog`.
- Enums listed as `tinyint` are `byte`-backed enums in `NcaafPickEm.Shared/Enums` (D-014) mapped with `HasConversion<byte>()`. The one exception is `AuditLog.Action`, stored by name in `nvarchar(40)` so the table stays readable and renumbering the enum cannot rewrite history.
- `datetime2` UTC columns are `DateTime` with a global `UtcDateTimeConverter` (D-016). `Games.KickoffEasternDate` and `UnmatchedGames.GameDate` are `DateOnly`/`date`.
- Guid keys are `ValueGenerated.Never`: IDs are created in code with `Guid.CreateVersion7()`.
- Every foreign key is `DeleteBehavior.Restrict` (D-018).
