# 06 - Agent Protocol

How the orchestrator briefs agents, how agents talk to each other, and how work is recorded. Agents are Opus or Sonnet at medium thinking; they can message each other directly.

## Briefing an agent

Every brief contains, in this order:

1. **Task card** pasted verbatim from `Phases/Phase-N-*.md`.
2. **Read-first list** from the card (always includes `00-README.md`, `05-Conventions.md`, and this file; usually the relevant WorkItems story and the sections of `02`, `03`, `04` the card cites).
3. **Branch name** and the current `main` commit to branch from.
4. **Contacts**: the agent names or task IDs of the upstream and downstream tasks currently in flight (from `STATUS.md`).
5. **What is already decided**: pointer to `DECISIONS.md`; the agent must not re-open decided items.
6. **Reporting instruction**: the agent's final message must follow the "Task report" template below.

Do not paste whole story files into a brief when the agent can read them; point at paths. Keep the brief under one screen plus the card.

## Working rules for agents

- Read the card, the story, and the cited plan sections before writing code. If the card conflicts with the story, the story wins; note the conflict in your report so the card gets fixed.
- Stay inside your folder ownership (`05-Conventions.md`). If you need a change in someone else's area, message the owner with the exact change you need. If nobody owns it right now, make the smallest possible change and record it in `STATUS.md`.
- Contract changes: update `03-API-Contracts.md` in your branch and message the counterpart agent (frontend or backend) before merging. The counterpart acknowledges or objects within their next turn.
- Schema changes: update `02-Data-Model.md`, add a migration named `Phase<N>_<Task>_<What>`, never edit an existing migration.
- Algorithm questions: if `04-Domain-Algorithms.md` does not answer it, propose an answer, write it into `DECISIONS.md` with the task ID, and continue. Do not block on the orchestrator for a call that has an obvious safe default.
- Escalate to the orchestrator only when: a decision would change a story's acceptance criteria, two agents disagree after one exchange, a spike result invalidates a plan assumption (for example, WASM load time), or an external credential is needed.
- Never mark a task Done without running the full test suite. Report test output honestly, including skipped or failing tests.
- Use fixtures for all development. Live provider keys are only used by P2-01 and P8-02/P8-03 with explicit orchestrator approval.

## Messaging between agents

Message format (keep to this so messages are scannable):

```
FROM: <task id> (<agent name>)
TO:   <task id> (<agent name>)
TYPE: Request | Answer | Heads-up | Blocker
RE:   <one line>
BODY: <what you need or are telling them, with exact names, routes, fields, or file paths>
NEEDS-REPLY: yes | no
```

Typical exchanges:
- Frontend -> Backend: "Request: `MyPicksResponse` needs `LockAtEasternDisplay`; proposed type string; I will not merge until you confirm."
- Domain -> Backend: "Heads-up: `GameSetGenerator.Generate` now returns `GenerationResult { Added, Removed, ExceedsMax }`; update the service call."
- Any -> Any: "Blocker: migration `Phase3_01_GameSetRules` conflicts with `Phase2_02_Teams` on `Conferences` FK; who owns Conferences?" (answer: P2-02 per the data model).

One exchange to resolve. If not resolved, both agents post the disagreement in `STATUS.md` under "Escalations" and the orchestrator decides.

## Task report (agent's final message)

```
TASK: <id> <title>
BRANCH: <name> @ <commit>
RESULT: Done | Done with notes | Blocked
DONE-WHEN CHECKLIST: <each item, checked or not, one line each>
TESTS: <command run>, <passed/failed/skipped counts>, <names of new test classes>
CHANGED DOCS: <paths or "none">
DECISIONS LOGGED: <ids or "none">
CONTRACT/SCHEMA CHANGES: <summary or "none">
OPEN ITEMS: <anything the next task must know>
SCREENSHOT: <path or "n/a">
```

The orchestrator does not accept "Done" without the test line and the checklist.

## Code review

- Every Sonnet task that touches `Domain/`, scoring, lock, authorization, or the poller is reviewed by an Opus agent before merge. The reviewer gets the diff and the relevant story, not the author's summary. Review output uses the same report template with `RESULT: Approve | Request changes`.
- Other Sonnet tasks are reviewed by the orchestrator (skim diff, run tests) or by a peer Sonnet.
- Opus tasks are reviewed by the orchestrator or a second Opus for Phase 0 and Phase 5 only.

## Phase gates

Before starting dependents of a phase, the orchestrator checks the phase file's "Phase exit criteria" list and records the gate in `STATUS.md` ("GATE Phase 2 passed @ <commit>").

## STATUS.md template

```
# Status

## Board
| Task | Title | Agent | Tier | State | Branch | Notes |
|---|---|---|---|---|---|---|
| P0-01 | Repo and solution scaffold | - | Opus | Todo | | |
... one row per task from every Phase file ...

## Gates
| Phase | Passed at commit | Date | Notes |

## Hot-spot edits (Program.cs, DbContext, app.css, service-worker.js)
| Date | Task | File | What |

## Escalations
| Date | Tasks | Question | Decision (link DECISIONS id) |
```

## DECISIONS.md template

```
# Decisions

Append only. Format: `D-NNN  YYYY-MM-DD  <task id>  <one-line decision>` then an indented rationale.

D-001  2026-09-18  plan   Frontend is Blazor WebAssembly PWA hosted by the API, pending P0-04 load-time spike.
       Rationale: see 01-Architecture.md. Fallback is Razor Pages + vanilla JS modules.
D-002  2026-09-18  plan   Multiple commissioners per league; authorization checks Membership.Role, never a single owner column.
       Rationale: Feature 01 as amended.
```
