# P8-03 - manual end-to-end run from an iPhone, driven by the simulator

**Status: Manual pending (operator).** No agent can do this: it needs a real iPhone on the tailnet,
a real tailnet account, and a real Home Screen install. The automated twins of this walkthrough are
`tests/NcaafPickEm.Api.Tests/Simulation/FullWeekSimulationTests.cs` (a whole fixture week against
the API) and `tests/NcaafPickEm.Simulator.Tests/AppInSimulatorModeTests.cs` (the simulator driving
the real app). Run this once before the real season starts - on any day, at any time: since P11-01
the week is simulated around *now* (D-182, D-185), so nothing here waits for a Saturday.

Save every screenshot in this folder, named exactly as the "Save as" column says (PNG, straight
from the iPhone's screenshot button - do not crop).

## Before you start (on a PC that is on the tailnet)

1. Give the app a real VAPID pair (README, "Web push (VAPID) keys") in
   `src/NcaafPickEm.Api/appsettings.Development.json`. The simulator borrows the same pair, so
   reminders it triggers reach the phone.
2. Start the app in Simulator mode and the simulator: `./tools/start-simulator.ps1` (README,
   "Simulating a week"). Before that, so invite links and cookies use the tailnet address, set:

   ```powershell
   $env:App__PublicOrigin = 'https://<pc>.<tailnet>.ts.net:8443'
   $env:App__BehindProxy = 'true'
   ```

3. Put the app on the tailnet, with identity headers, from the same PC:

   ```powershell
   tailscale serve --bg --https=8443 http://localhost:5204
   ```

4. In the simulator (<http://localhost:5299>): **Create sandbox** with the defaults. Then **Open
   the app** (signs this PC's browser in as Michael, the commissioner) and create an invite on the
   league's Invites page. Note the six-digit code.

## On the iPhone

Open `https://<pc>.<tailnet>.ts.net:8443/` in Safari over Tailscale; it signs you in as your own
tailnet account. "Simulator" below means a button on the dashboard at <http://localhost:5299>.

| # | What to do | What to expect | Save as |
|---|---|---|---|
| 1 | Open the app in Safari over Tailscale | The league picker, already signed in as you, no login screen, no horizontal scrolling at any width | `e2e-01-home.png` |
| 2 | Open Profile (`/me`) | Your tailnet login shown as the account email; no sign-out button | `e2e-02-signed-in.png` |
| 3 | Share → "Add to Home Screen", then open the installed app | Standalone (no Safari chrome), same signed-in session | `e2e-03-home-screen.png` |
| 4 | Profile → Notifications → "Turn on notifications", allow the prompt | "Notifications are on"; then "Send test notification" delivers a banner | `e2e-04-notifications-on.png` |
| 5 | Home → "Join by code" with the code from step 4 above | You are in Sim League, week 1, "Not started", with a lock minutes away | `e2e-05-joined.png` |
| 6 | On the PC as Michael: Members → promote yourself (the phone's account) to commissioner; then Configure → "This week's games" on the phone | The set the Sunday job generated (Top 25 + SEC games), close-line and conference games elevated | `e2e-06-week-games.png` |
| 7 | Add one of the simulator's "no set" games by hand, then give another game a 25-point override | Both show in the set; the override shows the elevated badge | `e2e-07-manual-add.png` |
| 8 | Picks: pick only two games, do not submit | Submit is disabled and reads "N picks left" | `e2e-08-picks-incomplete.png` |
| 9 | Simulator: Jobs → **FridayMemberReminder** → Run | A "Pick reminder" push arrives on the phone | `e2e-09-friday-reminder.png` |
| 10 | Simulator: Jobs → **FridayCommissionerSummary** → Run | An "Unsubmitted picks" push arrives, naming you and whoever else has not submitted | `e2e-10-commissioner-summary.png` |
| 11 | Pick the rest and Submit; Simulator: league card → **Fill everyone** with "leave one incomplete" | Status pill "Submitted"; the simulator's member table shows everyone Submitted but one | `e2e-11-picks-submitted.png` |
| 12 | Simulator: **Lock now** | The picks page is read-only; the dashboard tab opens; everyone's picks are visible | `e2e-12-locked.png` |
| 13 | Open the dashboard | Games ordered by opposite picks, the names matching the simulator's pick grid; no-pick members listed | `e2e-13-dashboard.png` |
| 14 | Leave the dashboard open while autoplay runs (first window kicks off at the lock) | It refreshes itself within a minute; live scores move; finished games turn Won/Lost without a reload | `e2e-14-dashboard-live.png` |
| 15 | Simulator: a live game in the set → edit → set 21-21 → **Set final** | `/admin/data` lists that game under "Needs review" with reason "Tie" | `e2e-15-needs-review.png` |
| 16 | Simulator: **Finish every started game** (move the lock earlier first if some have not kicked off) | Every game reads Final; the week is still not complete because of the tie | `e2e-16-all-final.png` |
| 17 | As commissioner, void one game (any) with a reason | The game greys out; everyone's points drop by its value | `e2e-17-void.png` |
| 18 | As commissioner, override the tied game to either team with a reason | The week flips to Complete; the week leaderboard shows a trophy | `e2e-18-override.png` |
| 19 | Open the week leaderboard, then the grid, and scroll the grid sideways | Correct/incorrect colours, the voided column greyed, the name column stays pinned | `e2e-19-grid.png` |
| 20 | Simulator: **Play out 2 weeks**; then open the season leaderboard | Ranks, points behind, weekly wins, and trend arrows | `e2e-20-season.png` |
| 21 | Open the league's audit view | The void and the override, newest first, with your name as actor | `e2e-21-audit.png` |

Afterwards: `tailscale serve --https=8443 off`.

## Recording the result

When every screenshot is saved, replace **Manual pending (operator)** in this file's first line
and in `Implementation/STATUS.md`'s P8-03 row with the date and the device
(for example `Verified 2026-10-02, iPhone 15 Pro, iOS 18.2`), and update the Feature 08
"Works in iOS standalone" row of `Implementation/07-Traceability.md` the same way.

If any step does not behave as described, do not adjust the table - raise it, because the automated
tests assert the same behaviour and the two must agree.
