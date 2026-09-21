# P8-03 - manual end-to-end run on the deployed server, from an iPhone

**Status: Manual pending (operator).** No agent can do this: it needs a real iPhone on the tailnet,
a real tailnet account, and a real Home Screen install. The automated twin of this walkthrough is
`tests/NcaafPickEm.Api.Tests/Simulation/FullWeekSimulationTests.cs`, which asserts every step below
against the same Week 7, 2026 fixtures. Run this once before the real season starts.

Save every screenshot in this folder, named exactly as the "Save as" column says (PNG, straight
from the iPhone's screenshot button - do not crop).

## Before you start (on the server, not the phone)

1. Deploy as usual on the server (`docker compose pull api && docker compose up -d api`) and
   confirm `https://<host>/health/ready` is healthy.
2. Point a **separate** simulation database at the app, or accept that this run leaves a demo
   league in the real one. The CLI below never touches the real league unless you name it.
3. Prepare the fixture week and give yourself a clean starting point:

   ```powershell
   $env:ConnectionStrings__Default = '<your simulation connection string>'
   dotnet run --project src/NcaafPickEm.Api -- simulate --week 7
   ```

   It seeds the Week 7, 2026 fixtures, the "Family League" demo league (Michael, Alyson, Dance,
   Alex, Daniel), and generates the week's Top 25 set. Keep the printed summary; every step below
   refers to it. Full option list: README → "Operate" → "Dry-run a week before the season".

## On the iPhone

Open the app through Tailscale, which signs you in as your own tailnet account, for steps 1-4,
then use `/auth/dev-login?user=<name>` in Safari to become each fixture member for the picking
steps.

| # | What to do | What to expect | Save as |
|---|---|---|---|
| 1 | Open `https://<host>/` in Safari over Tailscale | The league picker, already signed in as you, no login screen, no horizontal scrolling at any width | `e2e-01-home.png` |
| 2 | Open Profile (`/me`) | Your tailnet login shown as the account email; no sign-out button | `e2e-02-signed-in.png` |
| 3 | Share → "Add to Home Screen", then open the installed app | Standalone (no Safari chrome), same signed-in session | `e2e-03-home-screen.png` |
| 4 | Profile (`/me`) → Notifications → "Turn on notifications", allow the prompt | "Notifications are on"; then "Send test notification" delivers a banner | `e2e-04-notifications-on.png` |
| 5 | As the commissioner, open the league → Configure → "This week's games" | The three Top 25 games the CLI generated, each 10 points | `e2e-05-week-games.png` |
| 6 | Add Maryland / Rutgers by hand, then set Iowa State / Kansas to 15 points | Both appear in the set; the 15-point game shows the elevated badge | `e2e-06-manual-add.png` |
| 7 | `/auth/dev-login?user=alyson`, open Picks, pick every game and Submit | Status pill reads "Submitted"; the footer confirms | `e2e-07-picks-submitted.png` |
| 8 | `/auth/dev-login?user=alex`, pick only two games, do not submit | Submit is disabled and reads "N picks left" | `e2e-08-picks-incomplete.png` |
| 9 | On the server: `... -- simulate --tick "2026-10-16T20:00:00-04:00"` | A "Pick reminder" push arrives on the devices of the members who have not submitted, and on nobody else's | `e2e-09-friday-reminder.png` |
| 10 | On the server: `... -- simulate --tick "2026-10-16T21:00:00-04:00"` | An "Unsubmitted picks" push arrives on the commissioner's device only, naming the same people | `e2e-10-commissioner-summary.png` |
| 11 | On the server: `... -- simulate --week 7 --lock` | The picks page is read-only; the dashboard tab opens | `e2e-11-locked.png` |
| 12 | Open the dashboard as Dance (`/auth/dev-login?user=dance`) | Michigan / Texas leads with Opposite Picks = Michael, Alyson, Alex, Daniel; Maryland / Rutgers shows Opposite Picks = Alex - exactly `WorkItems/Overview.txt` | `e2e-12-dashboard-dance.png` |
| 13 | Open the dashboard as Alyson | Michigan / Texas shows Opposite Picks = Dance; Maryland / Rutgers shows Alex | `e2e-13-dashboard-alyson.png` |
| 14 | On the server: `... -- simulate --week 7 --snapshot 3`, leave the dashboard open | It refreshes itself within a minute; finished games turn Won/Lost without a reload | `e2e-14-dashboard-live.png` |
| 15 | On the server: `... -- simulate --week 7 --snapshot 4` | `/admin/data` lists Iowa State / Kansas under "Needs review" with reason "Tie" | `e2e-15-needs-review.png` |
| 16 | On the server: `... -- simulate --week 7 --snapshot 6` | Every game reads Final; the week is still not complete because of the tie | `e2e-16-all-final.png` |
| 17 | As commissioner, void one game (any) with a reason | The game greys out; everyone's points drop by its value | `e2e-17-void.png` |
| 18 | As commissioner, override Iowa State / Kansas to Kansas with a reason | The week flips to Complete; the week leaderboard shows a trophy | `e2e-18-override.png` |
| 19 | Open the week leaderboard, then the grid, and scroll the grid sideways | Correct/incorrect colours, the voided column greyed, the name column stays pinned | `e2e-19-grid.png` |
| 20 | Open the season leaderboard | Ranks, points behind, weekly wins; trend arrows once a second week is complete | `e2e-20-season.png` |
| 21 | Open `/api/leagues/{id}/audit` from the league's audit view | The void and the override, newest first, with your name as actor | `e2e-21-audit.png` |

## Recording the result

When every screenshot is saved, replace **Manual pending (operator)** in this file's first line
and in `Implementation/STATUS.md`'s P8-03 row with the date and the device
(for example `Verified 2026-10-02, iPhone 15 Pro, iOS 18.2`), and update the Feature 08
"Works in iOS standalone" row of `Implementation/07-Traceability.md` the same way.

If any step does not behave as described, do not adjust the table - raise it, because the automated
simulation asserts the same behaviour and the two must agree.
