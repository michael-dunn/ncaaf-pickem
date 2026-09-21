# NCAAF Pick Em — Implementation Plan

This folder is the orchestrator's reference for delivering all 13 feature stories in `../WorkItems/`.
The stories are the requirements. This plan is how the work is cut, ordered, assigned, and verified.

## How to use this folder

| File | Purpose | Who reads it |
|---|---|---|
| `00-README.md` | This page. Goals, phase map, how to run the plan. | Orchestrator, every agent at start |
| `01-Architecture.md` | Stack decisions, solution layout, runtime shape, key patterns. | Every agent |
| `02-Data-Model.md` | Tables, keys, invariants. Single source of truth for schema. | Backend and domain agents |
| `03-API-Contracts.md` | Every endpoint, route, auth scope, DTO name. Lets frontend and backend build in parallel. | Frontend and backend agents |
| `04-Domain-Algorithms.md` | Exact rules for game set generation, point resolution, lock, scoring, dashboard ordering, ranking, week calendar. | Domain agents, test writers |
| `05-Conventions.md` | Code style, folder ownership, testing rules, commits, definition of done. | Every agent |
| `06-Agent-Protocol.md` | How agents are briefed, communicate, hand off, escalate, and record decisions. | Orchestrator, every agent |
| `07-Traceability.md` | Every acceptance-criteria group in the 13 stories mapped to task IDs and the test that proves it. | Orchestrator for sign-off |
| `Phases/Phase-N-*.md` | Task cards: scope, inputs, deliverables, tests, agent tier, who to talk to. | Assigned agents |
| `DECISIONS.md` | Append-only log of decisions made during implementation. | Everyone; write when you decide something not in the plan |
| `STATUS.md` | Live task board. One line per task, updated by the agent that owns it. | Orchestrator |
| `AGENT-NOTES.md` | Living file of practical facts every implementation agent needs (environment, git workflow, composition root, auth, jobs, push, providers, fixtures). | Every agent, after `00`/`05`/`06` |
| `reviews/security-review.md` | P8-01's security and correctness review: every route's auth scope and CSRF cover, the three "never" rules, rate limiting, dependency and secret audits, findings and their resolution. | Orchestrator for sign-off; any agent adding an endpoint |
| `reviews/operator-checklist.md` | Every "Manual pending (operator)" item across the whole plan, in one ordered checklist with exact steps or the doc that has them. | The operator, before/during first real deploy |
| `spikes/providers.md` | P2-01's live-capture spike against the real ESPN scoreboard and CFBD APIs: tier confirmation, corrections to the assumed contract, the verified team-alias draft. | Provider/domain agents, orchestrator |
| `spikes/wasm-load-time.md` | P0-04's measured Blazor WASM load-time spike (cold/warm, localhost) against the 2 s/5 s budget, plus the pending phone-over-Tailscale measurement. | Orchestrator, anyone touching client startup |
| `screenshots/` | 375px UI screenshots, one per page/state, named `p<task>-<state>-375.png`. | Orchestrator for sign-off; P8-04 traceability |
| `screenshots/e2e/README.md` | The 21-step manual iPhone walkthrough of a full simulated week on the deployed server (P8-03), with exact screenshot filenames to save. | Operator, before the real season starts |

Rule of precedence when documents disagree: **WorkItems story > 04-Domain-Algorithms > 02-Data-Model / 03-API-Contracts > Phase task card > DECISIONS.md.** If a story is ambiguous, the algorithm file decides; if the algorithm file is silent, log a decision in `DECISIONS.md` and continue.

## What we are building (one paragraph)

A mobile-first PWA for a family college-football pick-em league. Commissioners configure rules that select Saturday FBS games each week and assign point values. Members tap winners before the first Saturday kickoff. At lock, an influence dashboard shows each member which games matter most against the rest of the league, with live scores. Games are scored automatically as they go final; weekly and season leaderboards update. Web push reminds people to submit. Stack is .NET on a home server behind Tailscale, SQL Server, sign-in from Tailscale Serve identity headers, CollegeFootballData for reference data and ESPN for live scores.

## Phase map

```
Phase 0  Foundation ─────────┬──────────────┬─────────────────┐
                             │              │                 │
Phase 1  Leagues & Members   │  Phase 2 Data Feed   │  Phase 3a Domain algorithms (pure, fixture-driven)
                             │              │                 │
                             └──────┬───────┴────────┬────────┘
                                    │                │
Phase 3b Game Sets & Points endpoints/UI             │
                                    │                │
Phase 4  Picks & Lock ──────────────┤                │
                                    │                │
Phase 5  Scoring & Leaderboard ─────┤◄───────────────┘
                                    │
Phase 6  Influence Dashboard ───────┤
                                    │
Phase 7  Notifications & PWA polish ┤
                                    │
Phase 8  Hardening, deploy, season simulation, traceability sign-off
```

| Phase | Stories | File |
|---|---|---|
| 0 Foundation | 08, 10, 13 | `Phases/Phase-0-Foundation.md` |
| 1 Leagues & Members | 01, 08, 13 | `Phases/Phase-1-Leagues.md` |
| 2 Data Feed | 09, 12 | `Phases/Phase-2-Data-Feed.md` |
| 3 Game Sets & Points | 02, 03, 13 | `Phases/Phase-3-GameSets-Points.md` |
| 4 Picks & Lock | 04, 05 (lock) | `Phases/Phase-4-Picks-Lock.md` |
| 5 Scoring & Leaderboard | 06, 07 | `Phases/Phase-5-Scoring-Leaderboard.md` |
| 6 Influence Dashboard | 05 | `Phases/Phase-6-Dashboard.md` |
| 7 Notifications & PWA | 11, 10 | `Phases/Phase-7-Notifications-PWA.md` |
| 8 Hardening & Deploy | 10, all | `Phases/Phase-8-Hardening-Deploy.md` |
| 10 Dev tooling (movable clock, demo-week controls) | 02, 04, 06 (testability) | `Phases/Phase-10-Dev-Tooling.md` |

Phases 1, 2, and 3a run in parallel once Phase 0 is done. Everything from Phase 3b onward depends on Phase 2's dev fixtures (P2-05) so that UI and endpoint work never waits on live provider data.

## Agent tiers

- **Opus (medium thinking)**: architecture, schema, domain algorithms, provider matching, scoring engine, dashboard algorithm, anything where a wrong call ripples. Also all code review of Sonnet output that touches domain logic.
- **Sonnet (medium thinking)**: endpoints against a written contract, UI pages against a written contract, tests from a written spec, migrations from a written schema, docs, fixtures, deploy scripts.

Each task card names a tier. The orchestrator may upgrade a Sonnet task to Opus if it stalls; it should not downgrade an Opus task.

## Running the plan

1. Orchestrator reads this file, `01`, `05`, `06`. `DECISIONS.md` (D-001 to D-008) and `STATUS.md` (full task board) are pre-seeded; P0-01 verifies them against the phase files.
2. Run Phase 0 with a single Opus agent (P0-01 through P0-06 are sequential-ish and shape everything).
3. Fan out Phases 1, 2, 3a to parallel agents. Each agent gets its task card, the files the card lists under "Read first", and the folder ownership rules.
4. Gate each phase on its "Phase exit criteria" section before starting dependents.
5. Phase 8's traceability audit (P8-04) is the final gate: every row in `07-Traceability.md` must point at a passing test or a manual check recorded in `STATUS.md`.

## Definition of done (project)

- Every acceptance criterion in the 13 stories is mapped in `07-Traceability.md` to a passing automated test or a recorded manual verification.
- The app runs on the home server over Tailscale HTTPS, installed to an iPhone home screen, signed in from the Tailscale identity headers, with push notifications, and a full simulated week (generate set, pick, lock, score, leaderboard) exercised end to end.
- No secrets in the repo. Nightly SQL backups configured.
