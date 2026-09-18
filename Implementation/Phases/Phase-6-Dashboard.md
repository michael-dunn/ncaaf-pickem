# Phase 6 - Influence Dashboard

Story: 05 Pick Lock and Influence Dashboard.
Depends on: Phase 4 (locked picks), Phase 2 (live scores), P5-01 (winner determination shared helper).

Read first: WorkItems 05 (including the worked example), `04-Domain-Algorithms.md` section 6, `03-API-Contracts.md` (Influence dashboard).

---

## P6-01 Influence calculator (domain)
Tier: Opus. Depends on: P4-01 (pick shapes), P5-01 (winner helper).

Deliverables
- `Domain/Dashboard/InfluenceCalculator` per `04` section 6, pure. Input records: viewer membership, active set games with status/scores/winner, picks of members active at lock. Output: ordered `Games`, `EveryoneAgrees`, `PointsSoFar`, `MaxRemaining`.
- Shared `WinnerResolver` (override > final score > none) if P5-01 has not already provided it; coordinate by message.

Done when
- `InfluenceCalculatorTests`: the Overview worked example produces exactly the two documented dashboards for Dance and Alyson; viewer excluded from own list; No Pick group; viewer-no-pick case stays in `Games` with both lists; ordering by count then points then kickoff; everyone-agrees split; Won/Lost marking; PointsSoFar and MaxRemaining; former member active at lock included; member who joined after lock excluded.

## P6-02 Dashboard endpoint
Tier: Sonnet (Opus review). Depends on: P6-01.

Deliverables
- `GET /api/leagues/{id}/weeks/{week}/dashboard` per contracts: before lock returns `IsAvailable=false` with `LockAtUtc`; after lock runs the calculator on current DB state; `ScoresMayBeStale` from the poller's fallback/failure flag (P2-04 exposes `ILiveScoreHealth`).
- Cheap enough to poll every 60 s: single query for picks, single for games.

Done when
- `DashboardTests` (before lock shape, after lock shape, stale flag propagation, member-only, no `asMember` parameter accepted), auth matrix.

## P6-03 Dashboard UI
Tier: Sonnet. Depends on: P6-02 contracts.

Deliverables (`Pages/Dashboard/DashboardPage.razor` at `/leagues/{id}/weeks/{week}/dashboard`)
- Before lock: countdown card to lock in local time (Eastern hint), "Dashboard opens at kickoff".
- After lock: header with "Points so far" and "Max remaining"; stale banner when `ScoresMayBeStale`.
- Game cards in server order: matchup with my pick marked, point value, status line (kickoff time / live score with period and clock / Final score), Won/Lost tint when Final, "Opposite picks (N)" as wrapping chips, "No pick" chips in muted style; when I have no pick, show both teams' pickers.
- Collapsed "Everyone agrees (N)" section at the bottom, expandable.
- Auto-refresh every 60 s while any game is not Final, using a timer that pauses when the tab is hidden; no full reload.

Done when
- Screenshots at 375px: pre-lock, live, all-final, stale banner. Verified against fixture snapshots advancing.

---

## Phase exit criteria
- Demo league (worked-example members) shows dashboards matching the Overview for Dance and Alyson, live scores advance with fixture snapshots, and Won/Lost states appear at Final.
