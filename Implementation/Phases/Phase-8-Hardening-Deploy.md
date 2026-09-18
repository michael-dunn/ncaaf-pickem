# Phase 8 - Hardening, Deploy, Season Simulation, Sign-off

Stories: 10 Hosting and Platform, plus verification of all 13.
Depends on: Phases 1 to 7 merged.

Read first: WorkItems 10, `07-Traceability.md`, `05-Conventions.md`.

---

## P8-01 Security and correctness review
Tier: Opus. Depends on: all feature phases.

Deliverables
- Walk every route in `03-API-Contracts.md` and confirm the auth matrix test exists and passes; add missing ones.
- Confirm CSRF filter covers every mutation; cookie flags; no `AllowAnonymous` leaks; `returnUrl` is relative-only; ProblemDetails never leak stack traces in Production.
- Confirm the three "never" rules: no picks visible before lock, no mutations after lock, no `DateTime.UtcNow` in Domain (grep).
- Rate limiting on `/auth/*` and `/api/invites/*/accept` (fixed window, generous).
- Dependency audit (`dotnet list package --vulnerable`).
- Findings fixed or logged as DECISIONS with rationale.

Done when
- Report in `Implementation/reviews/security-review.md` with each check and its evidence; suite green.

## P8-02 Home server deployment
Tier: Sonnet (orchestrator supervises credentials). Depends on: P0-01, P7-01.

Deliverables in `deploy/`
- `install-service.ps1`: publish Release (trimmed, Brotli), install as a Windows service (or provide `Dockerfile` + compose if the operator prefers), environment variables from a local `.env` not in git.
- Tailscale HTTPS: `tailscale cert <host>` steps, Kestrel bound to the cert, renewal note. Google OAuth redirect URI documented as `https://<host>/auth/callback/google`.
- `backup.sql` + scheduled task: nightly `BACKUP DATABASE` to a backups folder, prune older than 30 days, verify restore once (documented).
- `deploy.ps1`: refuses to run between Saturday 10:00 ET and Sunday 03:00 ET unless `-Force`; runs migrations; restarts service; hits `/health/ready`.
- `appsettings.Production.template.json` complete.

Done when
- App reachable from a phone on the tailnet over HTTPS, Google login round-trips, service survives a reboot, backup file appears the next morning. Record in STATUS.md with dates.

## P8-03 End-to-end season simulation
Tier: Opus. Depends on: everything.

Deliverables
- `tests/NcaafPickEm.Api.Tests/Simulation/FullWeekSimulationTests`: with fixtures and `TimeProvider` control: create league, invite 5 members (worked-example names), Tuesday regenerate, members pick (one leaves 2 unpicked, one never starts), Friday 20:00 reminders sent to the right people, Friday 21:00 summary to commissioners, Saturday lock -1h reminder, lock job (Incomplete for the two), dashboard for Dance and Alyson matches Overview, snapshots 1 to 6 applied with scoring after each, one void and one override, week Complete, season leaderboard and grid correct, snapshot written, second week seeded to assert trend arrows.
- A `Development`-only "Simulate" page or CLI (`dotnet run -- simulate --week 7 --snapshot 3`) so the operator can drive fixtures by hand on the home server before the real season.
- Manual run of the same flow on the deployed server from an iPhone; record screenshots in `Implementation/screenshots/e2e/`.

Done when
- Simulation test green; manual run recorded.

## P8-04 Traceability audit and docs
Tier: Sonnet. Depends on: P8-01 to P8-03.

Deliverables
- Fill a "Result" column for every row in `07-Traceability.md` (test name + pass, or manual date + device). Any row without proof becomes a task; loop until none remain.
- Root `README.md` final: architecture summary, run locally, run tests, deploy, operate (data status page, corrections, backups, Saturday rules).
- `Implementation/spikes/` and `reviews/` indexed from `00-README.md`.

Done when
- Every traceability row has proof; orchestrator records "GATE Phase 8 passed" in STATUS.md.

---

## Phase exit criteria (project done)
- All rows in `07-Traceability.md` proven.
- Deployed on the home server, installed on the family's phones, notifications on, first real week's game set generated from live CFBD data with the CFBD counter well under 1,000.
