# Phase 7 - Notifications and PWA polish

Stories: 11 Notifications, 10 (PWA acceptance).
Depends on: Phase 4 (statuses, lock time), P3-04 (add/remove events), P0-06 (scheduler).

Read first: WorkItems 11, `04-Domain-Algorithms.md` section 11, `03-API-Contracts.md` (Push), `02-Data-Model.md` (Notifications).

---

## P7-01 Push subscriptions and sender
Tier: Opus. Depends on: P0-03.

Deliverables
- VAPID key generation script (`deploy/generate-vapid.ps1`) and config keys `Push__*`.
- Endpoints per contracts: vapid public key, subscription upsert (by Endpoint), delete, status, notification log (Commish).
- `IPushSender` with `WebPushSender` (WebPush NuGet) and `FakePushSender` for tests; payload JSON `{ title, body, url, tag }`; TTL 1 hour for reminders.
- Delivery policy per `04` section 11: 404/410 deletes subscription and logs Expired; other failures retry 3x over 15 minutes then Failed; every attempt logged to `NotificationLog`.
- `NotificationService.SendToUser(userId, type, leagueId, week, content)` fanning out to all of the user's subscriptions.

Done when
- `PushSubscriptionTests` (upsert idempotent, delete, status), `PushDeliveryTests` with fake transport (410 cleanup, retry then Failed, log rows), auth.

## P7-02 Service worker, settings page, notification routing
Tier: Sonnet. Depends on: P0-04, P7-01 contracts.

Deliverables
- `service-worker.js`: `push` handler showing the notification with `data.url`; `notificationclick` focusing an existing client or opening `url` within the PWA scope (standalone on iOS).
- `wwwroot/js/push.js`: detect support, detect iOS-not-installed (`navigator.standalone === false` on iOS), request permission on tap, `PushManager.subscribe` with the VAPID key, send to API, unsubscribe flow.
- **Notifications** section on the Profile page (`/me`): "Turn on notifications" / "Turn off notifications", iOS install instructions when not installed, "Notifications are off on this device" state when the server reports no subscription for this endpoint.
- Icons and badge for notifications in the manifest.

Done when
- Manual on iPhone home-screen app: subscribe, receive a test push (add a Commish-only `POST /api/push/test` in Development only), tap opens the picks page in standalone. Manual on desktop Chrome as a second device. Screenshots.

## P7-03 Reminder jobs and event notifications
Tier: Sonnet (Opus review). Depends on: P7-01, P4-04, P3-04, P0-06.

Deliverables
- `FridayMemberReminderJob` (Fri 20:00 ET), `FridayCommissionerSummaryJob` (Fri 21:00 ET), `SaturdayReminderOneShot` at `LockAtUtc - 1h` (re-read each minute from `WeekGameSets` so lock changes are honored), all per `04` section 11 with send-time evaluation and once-per-week enforcement via the filtered unique index.
- Event handlers: `GameAddedToSet` -> "N new games" to members who were Submitted (coalesce per regeneration); `GameRemovedFromSet` -> to members with a pick on it.
- Message texts exactly as in Feature 11's catalog; URLs point at the picks page or league home.

Done when
- `ReminderJobTests`: recipients by status at send time; submitted at 7:59 gets nothing; no set = nothing; locked week = nothing; second run same week = Skipped; Saturday one-shot moves when lock moves; commissioner summary only when someone is unsubmitted and lists names; GamesAdded coalesced; GameRemoved targeted.

---

## Phase exit criteria
- On a phone with the installed PWA: turning on notifications works, a Friday reminder fires in a time-shifted test (use `TimeProvider` override in Development), and adding a game to a submitted week sends a push within seconds.
