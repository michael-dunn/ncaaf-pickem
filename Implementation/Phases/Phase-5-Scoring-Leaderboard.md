# Phase 5 - Scoring and Leaderboard

Stories: 06 Scoring, 07 Leaderboard, 13 (after-lock schedule changes).
Depends on: Phase 4 (locked weeks with picks), Phase 2 (`GameWentFinal` events, fixture score snapshots).

Read first: WorkItems 06, 07, `04-Domain-Algorithms.md` sections 7 and 8, `03-API-Contracts.md` (Scoring, Leaderboard), `02-Data-Model.md` (Scoring).

---

## P5-01 Week scorer (domain + service)
Tier: Opus. Depends on: P4-02, P2-03.

Deliverables
- `Domain/Scoring/WeekScorer` per `04` section 7: pure full recompute returning `WeekResult` rows and `IsWeekComplete`.
- `ScoringService`: subscribes to `GameWentFinal`, `ResultOverridden`, `GameVoided`; loads the affected league weeks; recomputes and upserts `WeekResults`; when a week first becomes Complete writes `SeasonStandingsSnapshots` for that `ThroughWeek` using `StandingsCalculator` (P5-03; stub the interface if P5-03 is not merged, coordinate via message).
- `NightlyRescoreJob` 04:30 Eastern: recompute every locked, non-complete week for safety.

Done when
- `WeekScorerTests`: correct earns locked value, wrong/no pick zero, voided excluded, not-final unscored, tie/no-winner unscored and flagged, idempotent recompute, Complete flag, post-midnight Final (fixture snapshot 6) counted in week 7.

## P5-02 Corrections: override result and void
Tier: Sonnet (Opus review). Depends on: P5-01.

Deliverables
- Endpoints per contracts: override-result (winner must be home or away; after lock only), void (after lock only), audit GET (member-visible).
- Both write `AuditLog` with actor, reason, and before/after, raise the corresponding event, and trigger rescoring.
- Handler for `GameNeedsVoidReview` (from P3-04): list on data status page "needs review" with one-tap Void.

Done when
- `OverrideTests` (rescore happens, audit visible to members, 409 before lock, 400 wrong team), `VoidTests` (excluded from scoring and `ActiveGameCount`, shows Voided in grid), auth matrix.

## P5-03 Standings and leaderboard queries
Tier: Opus. Depends on: P5-01, P1-01.

Deliverables
- `Domain/Leaderboard/StandingsCalculator` per `04` section 8: season rows with competition ranking, points behind, weekly wins, trend from snapshots; week rows including former members; grid cells.
- `LeaderboardService` with efficient queries (one query for results, one for memberships, one for snapshots) and endpoints per contracts: season leaderboard, week leaderboard, grid (403 before lock), league weeks (navigable = generated).
- Mid-season joiners: no rows for weeks before `JoinedWeek`; season total from their weeks only.

Done when
- `StandingsCalculatorTests` (ties share rank and skip, behind leader, weekly wins with ties, trend up/down/same/none, late joiner, former member excluded from season but present in week), `LeaderboardPerfTests` (seed 50 members x 15 weeks x 20 games; season endpoint under 1 s locally), auth matrix.

## P5-04 Leaderboard UI
Tier: Sonnet. Depends on: P5-03 contracts.

Deliverables (`Pages/Leaderboard/`)
- **Season** (`/leagues/{id}/leaderboard`): rows with rank, trend arrow, name, total, behind, wins; my row highlighted; "Through Week N".
- **Week** (`/leagues/{id}/weeks/{week}/leaderboard`): prev/next week controls limited to generated weeks, "Current" jump, "In Progress" label when not complete, trophy on winners, correct/total, former-member marker.
- **Grid** (`/leagues/{id}/weeks/{week}/grid`): games as rows (pinned first column with matchup and result), members as columns, cells colored Correct/Incorrect/Pending/NoPick, voided rows greyed; horizontal scroll inside the grid only; hidden with a lock message before lock.

Done when
- Screenshots at 375px for all three; grid pinned column verified on iOS Safari.

## P5-05 Commissioner corrections UI and audit view
Tier: Sonnet. Depends on: P5-02 contracts.

Deliverables
- On the commissioner week view (P3-05 page) after lock: per-game "Set result" (pick winner, reason) and "Void" (reason) actions behind confirm sheets.
- **Audit** page (`/leagues/{id}/audit`) visible to all members: newest first, actor, action, summary.
- Data status page "needs review" entries link here.

Done when
- Screenshots; an override performed in the UI changes the leaderboard immediately.

---

## Phase exit criteria
- Advancing fixture snapshots 1 to 6 scores the demo league week, leaderboard and grid update, a void and an override rescore correctly with audit entries, and season trend arrows appear after a second Complete week (use a second fixture week or a seeded snapshot).
