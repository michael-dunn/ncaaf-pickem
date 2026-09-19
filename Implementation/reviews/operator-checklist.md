# Operator checklist

Every `Manual pending (operator)` item recorded anywhere in `Implementation/STATUS.md`, collected into
one ordered checklist. No agent in this environment has a phone, a tailnet, a home server, or a real
Google account, so none of these can be done by an agent — an operator (a person) has to run them.
Work top to bottom; later items depend on earlier ones (the app has to be deployed before you can
check it from a phone). Record the result of each item back in the `STATUS.md` row named in the
"Source" column, and, where noted, in `Implementation/07-Traceability.md`'s Result column too.

| # | Item | Steps | Source (STATUS.md row) |
|---|---|---|---|
| 1 | Real Google sign-in round trip (local dev) | `README.md` → "Google OAuth dev setup" (create an OAuth client, redirect URI `https://localhost:7092/auth/callback/google`, put the client id/secret in user secrets, run `dotnet run --project src/NcaafPickEm.Api`, sign in at `/login`). | P0-03 |
| 2 | iPhone home-screen install + load-time measurement over Tailscale | `Implementation/spikes/wasm-load-time.md` → "Operator to-do": join the phone to the tailnet, open the app's Tailscale HTTPS URL in Safari, Add to Home Screen, open from the icon (standalone), time cold and repeat loads against the 5 s / 2 s budget recorded for localhost. | P0-04 (also referenced by Gate 0) |
| 3 | First-time home server deployment (steps 1-9, `README.md` → "Deploy" → "First-time setup") | (1) Install Tailscale, note the tailnet hostname. (2) `./deploy/renew-cert.ps1 -TailnetHost <host> -CertDir C:\NcaafPickEm\cert`. (3) Add the redirect URI + JS origin to the Google OAuth client. (4) `cp deploy/.env.example deploy/.env` and fill in every key. (5) `./deploy/generate-vapid.ps1`, put the pair in `.env`. (6) `./deploy/install-service.ps1`. (7) `./deploy/register-backup-task.ps1` and `./deploy/register-renew-cert-task.ps1 -TailnetHost <host>`. (8) Run the first backup and `./deploy/restore-verify.ps1`. (9) From a phone on the tailnet, open the app in Safari, sign in with Google, Add to Home Screen. | P8-02 |
| 4 | Deployed-server verification (the P8-02 "Done when" items, steps 10-12) | (10) Confirm the app is reachable from a phone on the tailnet over HTTPS — record the date. (11) Confirm Google login round-trips against the deployed server (not localhost) — record the date. (12) Reboot the home server and confirm the `NcaafPickEm` service auto-starts and `/health/ready` comes back healthy — record the date. Exact commands: `README.md` → "Deploy"/"Operate". | P8-02 |
| 5 | First nightly backup appears | The morning after step 3/6, confirm a `.bak` file landed in `BACKUP_FOLDER` (`deploy/.env`) at 03:45 local time; run `./deploy/restore-verify.ps1` once to prove it actually restores. Record the date. | P8-02 |
| 6 | Notifications on the installed iPhone PWA | `README.md` → "Notifications on iPhone" (7 numbered steps): set real VAPID keys, add the app to the Home Screen, open it from the icon (not Safari), turn on notifications on `/me`, accept the permission prompt, have a commissioner send a test push (`POST /api/push/test` or the Development-only button), confirm the banner appears and tapping it foregrounds the standalone app at the target page. Record in `STATUS.md`'s P7-02 row. | P7-01, P7-02 (also referenced by Gate 7 for the time-shifted Friday reminder on the deployed server) |
| 7 | iOS Safari leaderboard grid — pinned column check | Open the week leaderboard grid page on an iPhone in Safari, scroll the grid horizontally, and confirm the "Game" column (matchup + result) stays pinned on the left edge throughout. Headless-Chromium mid-scroll screenshot (`Implementation/screenshots/p5-04-grid-scrolled-375.png`) is the best available substitute an agent could produce. | P5-04 |
| 8 | Full end-to-end season simulation walkthrough, from an iPhone, on the deployed server | `Implementation/screenshots/e2e/README.md` — 21 numbered steps (sign in, install, notifications, configure the week, pick, Friday reminders, lock, dashboard, live scores, needs-review, void, override, leaderboard, grid, season standings, audit log), each with the exact screenshot filename to save. Run once before the real season starts. Update the first line of that file, `STATUS.md`'s P8-03 row, and Feature 08's "Works in iOS standalone" row in `07-Traceability.md` with the date and device once done. | P8-03 |

## Not on this list (accepted, not manual-pending)

- The last-commissioner-race and `Invite.Uses` concurrency findings (STATUS.md Escalations,
  2026-09-18) are an accepted risk at family scale, not an operator action item.
- `/api/admin/fixture/*` being `Authenticated`-only rather than commissioner-gated is accepted
  because it is Development/Testing-only (security-review.md §10).
- The unmatched-game resolve picker being a raw GUID box is a UI polish item, not a manual
  verification — tracked as a possible follow-up card, not here.
