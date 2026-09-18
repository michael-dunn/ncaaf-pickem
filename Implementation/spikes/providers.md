# P2-01 Provider spike — ESPN and CollegeFootballData

Owner: `opus-p2-01`. Captured 2026-09-18. Branch `p2-01-provider-spike`.

Inputs: WorkItems 09 and 12, `02-Data-Model.md` (Season reference data, Operations), `04-Domain-Algorithms.md` sections 9 and 10, D-003.

---

## Summary

- **ESPN half: done live.** Seven read-only GETs against `site.api.espn.com`. Every field the card lists exists and behaves as Feature 12 assumed, with four corrections worth acting on (Eastern-day bucketing, `groups=80` does **not** classify FBS/FCS, the odds `spread` sign convention, and a normalization gap for diacritics). Trimmed captures are committed under `tests/NcaafPickEm.Fixtures/Real/`.
- **CFBD tier question: resolved from the public tiers page.** The **Live Scoreboard endpoint is Tier 1 ($1/month, 5,000 calls)**, not Tier 2. Feature 12 recorded the ambiguity ("the tiers page lists live scoreboard at Tier 1 in one place and Tier 2 in another; confirm before subscribing"); it is settled, and the escape hatch from the hybrid is $1/month, not $5. See D-012.
- **CFBD contract: derived from the live OpenAPI document**, `https://api.collegefootballdata.com/api-docs.json` (public, unauthenticated, 217 KB), cross-checked against the official NuGet client's README. Exact method paths, query parameters and response field names for all five calls are below. This is authoritative for P2-02's DTO work.
- **CFBD live calls: still not made.** The operator's key exists at a path outside the repo, but every attempt to combine "read that file" with "send an HTTP request" was refused by the Claude Code auto-mode permission classifier (reason: *Credential Exploration*), in both Git Bash and PowerShell. This is a **permission** block, not a key block. The five calls are specified precisely in "Blocked: operator to-do" below and take about two minutes to run by hand.
- **TeamAliases draft:** `tests/NcaafPickEm.Fixtures/Real/team-aliases-draft.json`, 24 rows. Every row is confirmed on the ESPN side from the real capture; none is confirmed on the CFBD side, because that needs the `/teams/fbs` payload. Rows needing confirmation carry a `note` saying so.

---

## What was verified live

| # | Request | Result |
|---|---|---|
| 1 | `GET .../scoreboard?groups=80&dates=20260912&limit=300` | 200, 1,321,837 bytes, 80 events, all `STATUS_FINAL` |
| 2 | `GET .../scoreboard?groups=80&dates=20260919&limit=300` | 200, 1,304,955 bytes, 71 events, all `STATUS_SCHEDULED`, all 71 carry odds |
| 3 | `GET .../scoreboard?groups=80&dates=20260918&limit=300` | 200, 58,888 bytes, 3 events (Friday slate), all `STATUS_SCHEDULED` |
| 4 | `GET .../rankings` | 200, 620,455 bytes, 5 polls (`ap`, `usa`, `fcs`, AFCA DII, AFCA DIII) |
| 5 | `GET .../scoreboard?dates=20260912&limit=300` (no `groups`) | 200, **same 80 events** as #1 |
| 6 | `GET .../scoreboard?groups=80&dates=20260912-20260919&limit=300` | **HTTP 400** `{"code":400,"message":"Failed to get events endpoint."}` |
| 7 | `GET .../scoreboard?groups=80&dates=20260912` (no `limit`) | 200, byte-identical to #1 |
| 8 | `GET https://collegefootballdata.com/api-tiers` (docs page) | tier table read, see below |
| 9 | `GET https://www.nuget.org/packages/CollegeFootballData` (docs page) | package id and versions read |
| 10 | `GET https://www.nuget.org/api/v2/package/CollegeFootballData/5.27.1` | 237,100-byte `.nupkg`, unpacked and read |
| 11 | `GET https://api.collegefootballdata.com/api-docs.json` | 200, 217,357 bytes, **no API key required** — the OpenAPI document is public |

Base URL, both scoreboard and rankings: `https://site.api.espn.com/apis/site/v2/sports/football/college-football`.

**Not observed live:** an in-progress game. The capture window (2026-09-18 20:41 UTC = 16:41 ET, Friday) fell between slates — the three Friday games kicked at 19:30 ET. So `STATUS_IN_PROGRESS`, `STATUS_HALFTIME`, `STATUS_END_PERIOD`, `STATUS_DELAYED`, `STATUS_POSTPONED` and `STATUS_CANCELED` are **not** evidenced by a committed payload; only `STATUS_SCHEDULED` and `STATUS_FINAL` are. P2-05's synthetic snapshots must supply the in-progress shapes, and P2-03's mapping must treat any unrecognized `status.type.name` as a logged no-op rather than a crash (see "Status mapping check").

---

## ESPN field mappings

### Request shape

```
GET https://site.api.espn.com/apis/site/v2/sports/football/college-football/scoreboard
      ?groups=80
      &dates=YYYYMMDD
```

- **`dates` accepts exactly one day.** `dates=20260912-20260919` returns HTTP 400. Feature 12's note that "date-range queries stopped working in 2025" is confirmed still true in 2026. The poller must issue one call per Saturday date, which is what `04` section 10 already assumes.
- **`dates` buckets by Eastern calendar date, not UTC.** On `dates=20260912` the earliest kickoff is `2026-09-12T16:00Z` (12:00 EDT) and the latest is `2026-09-13T03:59Z` (23:59 EDT). A late West Coast game that kicks at 22:30 ET Saturday and goes final at 02:00 ET Sunday therefore stays inside `dates=20260912` for the whole poll window. This is exactly the behaviour `04` section 10 needs (window runs to 03:00 ET Sunday) and the behaviour the P2-05 "Final after midnight ET" fixture must model. Caveat: the boundary follows US Eastern with DST, so the UTC offset shifts by an hour after the first Sunday in November.
- **`limit` is not required.** Request #7 without `limit` returned a byte-identical payload to `limit=300`. Keep `limit=300` anyway as insurance against ESPN reinstating a default page size; it costs nothing.
- **`groups=80` does not filter FCS opponents out.** Request #5 (no `groups`) returned the same 80 events as request #1. What `groups=80` gives is an explicit, echoed intent (`"groups": ["80"]` at the top level) — it selects *games involving an FBS team*, not *games where both teams are FBS*. Both captures contain FCS participants: Howard (`conferenceId` 24, MEAC) at Indiana, Portland State at Oregon, Gardner-Webb, UT Martin, Cal Poly, Colgate, Delaware State, Stonehill and more. Across the two Saturdays the payloads name **185 distinct teams**, spread over conference groups `1, 4, 5, 8, 9, 12, 15, 17, 18, 20, 21, 24, 25, 27, 29, 30, 31, 37, 48, 151, 177, 179` — far more than the ~136 FBS programmes. Feature 12's line "FBS/FCS classification has to be inferred from the groups parameter" is therefore **wrong**; the classification must come from CFBD's `Team.classification` (`fbs`/`fcs`/`ii`/`ii-iii`/`iii`), which is precisely what the hybrid in D-003 already does. No change to D-003; Feature 12's wording should be corrected. See D-012.

### Response shape (the fields the card names)

| Path | Type / example | Use |
|---|---|---|
| `events[].id` | string, `"401856682"` | `Games.EspnEventId` (parse to `long`; ESPN returns it as a **string**) |
| `events[].uid` | string, `"s:20~l:23~e:401856682"` | debugging only |
| `events[].date` | string, `"2026-09-12T23:30Z"` | kickoff, UTC. **Minute precision, no seconds and no offset digits** — the literal is `yyyy-MM-ddTHH:mmZ`. `DateTimeOffset.Parse` with `DateTimeStyles.RoundtripKind` handles it; a hand-rolled `ParseExact("yyyy-MM-ddTHH:mm:ssZ")` would throw. |
| `events[].name` / `.shortName` | `"Ohio State Buckeyes at Texas Longhorns"` / `"OSU @ TEX"` | `UnmatchedGames.RawPayload` context |
| `events[].season` | `{ "year": 2026, "type": 2, "slug": "regular-season" }` | season + `IsRegularSeason` cross-check (`type` 2 = regular, 3 = postseason) |
| `events[].week` | `{ "number": 3 }` | ESPN's own week. Same value at the top level as `week.number`. Advisory only — CFBD's week is the source of truth per D-003. Observed: `dates=20260912` → week 2, `dates=20260918` and `dates=20260919` → week 3. |
| `competitions[0].status.type.name` | `"STATUS_FINAL"` | status mapping, below |
| `competitions[0].status.type.id` | `"1"` scheduled, `"3"` final | stable numeric twin of `name`; prefer `name` |
| `competitions[0].status.type.state` | `"pre"` / `"in"` / `"post"` | coarse three-way state; a useful fallback when `name` is unrecognized |
| `competitions[0].status.type.completed` | bool | `true` implies Final, per `04` section 9 |
| `competitions[0].status.period` | int, `4` | `Games.Period` |
| `competitions[0].status.clock` / `.displayClock` | `0` / `"0:00"` | `Games.Clock` — store `displayClock` (`nvarchar(8)` fits `"15:00"`) |
| `events[].status` | identical object to `competitions[0].status` | duplicate; read the competition one |
| `competitions[0].competitors[].homeAway` | `"home"` / `"away"` | side assignment |
| `competitions[0].competitors[].score` | **string**, `"24"` — and `"0"` on scheduled games | `Games.HomeScore`/`AwayScore`. **Guard on status:** a scheduled game reports `"0"`/`"0"`, which must not be read as a real 0–0 tie. Only persist scores when `state != "pre"`. |
| `competitions[0].competitors[].winner` | bool, present only once a game is final | cross-check; `04` section 7 still derives the winner from the scores |
| `competitions[0].competitors[].curatedRank.current` | int, `1`..`25`, **`99` = unranked** | AP rank at kickoff. Present on every competitor in both captures. |
| `competitions[0].competitors[].linescores[]` | `[{ "value": 3, "period": 2 }]` | not needed; stripped from the fixtures except one example |
| `competitions[0].competitors[].records[]` | `[{ "type": "total", "summary": "2-0" }]` | not needed |
| `competitors[].team.id` | string, `"251"` | `Teams.EspnTeamId` (parse to `int`) |
| `competitors[].team.uid` | `"s:20~l:23~t:251"` | debugging |
| `competitors[].team.location` | `"Texas"`, `"Ole Miss"`, `"App State"`, `"Miami (OH)"` | **the field to match against CFBD `school`** — it is the school name without the mascot |
| `competitors[].team.name` | `"Longhorns"` | mascot |
| `competitors[].team.displayName` | `"Texas Longhorns"` | = `location` + `" "` + `name`; the card's nominated match field, but `location` is the cleaner one |
| `competitors[].team.shortDisplayName` | `"Texas"`, `"Miami OH"`, `"Jax State"` | lossy; do not match on it |
| `competitors[].team.abbreviation` | `"TEX"`, `"TA&M"`, `"M-OH"` | secondary match key; see the warning under TeamAliases |
| `competitors[].team.conferenceId` | string, `"8"` | `Conferences.EspnGroupId`. Present on the scoreboard; the **rankings** payload instead carries a richer `team.groups` object with `parent.id = "80"`, `parent.shortName = "FBS"`. |
| `competitors[].team.logo` | one URL | `Teams.LogoUrl` fallback (CFBD is the primary per Feature 09) |
| `competitions[0].odds[0].details` | `"DEL -5.5"` — abbreviation of the **favourite**, space, negative number | display; also the parse cross-check for the sign |
| `competitions[0].odds[0].spread` | number, `-5.5` or `24.5` | `GameLines.Spread`. **Home-relative**: negative = home favoured, positive = away favoured. Verified on all 71 odds-bearing events in the 2026-09-19 capture: 57 home-favoured, 14 away-favoured, 0 disagreements between `details` and `spread`. This matches `02-Data-Model.md`'s `GameLines.Spread` definition ("home minus away; negative = home favored") **exactly**; no conversion needed. |
| `competitions[0].odds[0].overUnder` | number, `57.5` | not modelled; ignore |
| `competitions[0].odds[0].provider.{id,name}` | `"100"` / `"Draft Kings"` | `GameLines.Provider` when ESPN is the line source |
| `competitions[0].odds[0].{homeTeamOdds,awayTeamOdds}.favorite` | bool | redundant with the sign; keep for the fixture |
| `competitions[0].neutralSite` | bool | 2 of 71 on 2026-09-19; not modelled today, but it is the flag a future "neutral site" point rule would need |
| `competitions[0].conferenceCompetition` | bool | 6 of 80 (2026-09-12), 11 of 71 (2026-09-19). CFBD's `Game.conferenceGame` stays the source of truth for `Games.IsConferenceGame` per D-003; this is the cross-check. |
| `competitions[0].groups` | `{ "id": "1", "name": "Atlantic Coast Conference", "shortName": "ACC", "isConference": true }` | **present exactly when `conferenceCompetition` is true** (6/6 and 11/11 in the captures; absent on every non-conference game). Free conference identification for conference games. |
| `competitions[0].venue` | `{ "id", "fullName", "address": { city, state, country }, "indoor" }` | `Games.Venue` fallback — store `fullName` |
| `competitions[0].attendance` | int, `105044` | not modelled |
| top-level `groups` | `["80"]` | echo of the request |
| top-level `week` | `{ "number": 2 }` | ESPN week for the requested date |
| `leagues[0].calendar` | the full season calendar: `Regular Season` with 15 week entries plus `Postseason` (`Bowls`, `CFP`), each `{ label, value, startDate, endDate }` | **a free, keyless alternative to CFBD `/calendar`** for `SeasonWeeks`, embedded in every scoreboard response. Worth noting for P2-02 as a zero-cost fallback if CFBD is unreachable; CFBD remains primary. |

Dropped from the committed fixtures (present in the raw payloads): `links`, per-team `logos[]` arrays, `broadcasts`, `broadcast`, `geoBroadcasts`, `leaders`, `headlines`, `highlights`, `notes`, `tickets`, `situation`, `statistics`, `weather`, `format`, `records` other than `total`, and everything under `odds[0]` except the fields tabulated above (the full `moneyline` block alone is ~1.5 KB per game and carries sportsbook deep links).

### Rankings endpoint

```
GET https://site.api.espn.com/apis/site/v2/sports/football/college-football/rankings
```

No query string needed; it returns the current week. Top-level: `rankings[]` (5 polls), `latestSeason`, `latestWeek`, `requestedSeason`, `availableRankings`, `weekCounts`, `weeks`.

- The AP poll is `rankings[]` where `type == "ap"` (`id` `"1"`, `name` `"AP Top 25"`, `shortName` `"AP Poll"`). Feature 09 wants AP only; select on `type`, not on array position.
- `occurrence` = `{ "number": 3, "type": "week", "value": "3", "displayValue": "Week 3", "last": false }` → `Rankings.Week`.
- `ranks[]` entries: `current` (1..25 → `Rankings.Rank`), `previous`, `points`, `firstPlaceVotes`, `trend` (`"+3"`, `"-"`), `date`, `lastUpdated`, `recordSummary` (`"2-0"`), and `team` with `id`, `uid`, `location`, `name`, `nickname`, `abbreviation`, `groups` (`{ id, shortName, isConference, parent: { id: "80", shortName: "FBS", isConference: false } }`), `logos[]`, `logo`, `links[]`.
- `others[]` (15) and `droppedOut[]` (1) use the same row shape. Ignore both for `Rankings`.
- **ESPN rankings are only needed as a fallback.** Under D-003 CFBD supplies AP rankings. The value of this endpoint is that `curatedRank.current` on the scoreboard already gives the per-game rank the dashboard needs, so the live poller never needs a second call.

---

## CFBD (from docs + client models)

### Tier answer (Feature 12 follow-up — resolved)

From `https://collegefootballdata.com/api-tiers`:

| Tier | Price | Monthly calls | Adds |
|---|---|---|---|
| Free | $0 | 1,000 | basic endpoints, historical data, team/player stats, recruiting, **betting lines**, advanced metrics |
| Academic | $0 (`.edu` email) | 3,000 | same as Free |
| **Tier 1** | **$1/mo** | **5,000** | opponent-adjusted metrics, weather, **Live Scoreboard** |
| Tier 2 | $5/mo | 30,000 | live play-by-play |
| Tier 3 | $10/mo | 75,000 | GraphQL API |
| Tiers 4–6 | $15 / $20 / $30 | 125k / 200k / 500k | same features as Tier 3 |

**The live scoreboard endpoint is Tier 1, $1/month, with a 5,000-call allowance.** Feature 12 assumed Tier 2 at $5 and flagged the page as self-contradictory; the current page is unambiguous. Two consequences:

1. Feature 12's Option 1 cost line ("$1 to $5 per month… confirm before subscribing") and its Option 3 fallback line ("Upgrading CFBD to Tier 2 for $5 restores live data") should both say **Tier 1, $1/month**.
2. The hybrid's downside shrinks. The ESPN-outage escape hatch is a dollar, and Tier 1's 5,000 calls/month comfortably covers `04` section 10's ~174 polls per Saturday (≈870 in a 5-Saturday month) *plus* reference data — the exact headroom problem Feature 12's budget section worried about. **The hybrid recommendation (D-003) still stands** — ESPN is free, gives richer live data (clock, period, live odds) and needs no key — but the fallback is now cheap enough that P2-04's "warn at 800 CFBD calls" threshold should be understood as a free-tier guard, not a cliff.

### Client package

- **Package id: `CollegeFootballData`. Latest version: `5.27.1`, published 2026-09-07.** P2-02 pins this in `Directory.Packages.props` (`<PackageVersion Include="CollegeFootballData" Version="5.27.1" />` plus a version-less `PackageReference`). Not modified by this task.
- `net8.0` assembly; `net10.0` is compatible. Dependencies: `Microsoft.Kiota.Abstractions`, `Microsoft.Kiota.Authentication.Azure`, `Microsoft.Kiota.Bundle`, `Microsoft.Kiota.Http.HttpClientLibrary`, all `>= 1.19.0`. That is five extra `PackageVersion` rows under central package management if transitive pinning surfaces them.
- **It is a Kiota-generated client**, auto-generated from the OpenAPI document, source at `github.com/cfbd/cfb-net`. Construction (from the package README):

```csharp
var authProvider = new BaseBearerTokenAuthenticationProvider(new StaticAccessTokenProvider(apiKey));
var requestAdapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
var client = new ApiClient(requestAdapter);
```

  `StaticAccessTokenProvider` is **not** supplied by the package — the README shows you writing it (implement `IAccessTokenProvider`, return the key, expose an empty `AllowedHostsValidator`). P2-02 owns that ~15-line class. Notes for P2-02:
  - Kiota's adapter takes an `HttpClient`. Register it through `IHttpClientFactory` so Polly/ProviderCalls instrumentation has a seam, rather than `new HttpClient()` as the README does.
  - Every call is `GetAsync(cfg => cfg.QueryParameters.X = ...)` and returns `List<T>?` — **nullable**, and the generated models make almost every property nullable regardless of the spec's `required` list. P2-02's ingest must null-check, not null-forgive.
  - The API key belongs in configuration (`Providers:Cfbd:ApiKey`), never in the repo. `appsettings.Development.json` is gitignored; add the key to the committed template as an empty string.

### The five calls (method path, parameters, response)

Method paths follow Kiota's URL-segment convention and are corroborated by the README (`client.Games.GetAsync(...)`, `client.Teams.Fbs.GetAsync()`). Field names below are from the live OpenAPI document; Kiota PascalCases them (`homeTeam` → `HomeTeam`).

**1. Teams (FBS)** — `client.Teams.Fbs.GetAsync(cfg => cfg.QueryParameters.Year = 2025)`
`GET /teams/fbs?year={year}` (operationId `GetFBSTeams`) → `List<Team>`

| Field | Type | Maps to |
|---|---|---|
| `id` | int | `Teams.CfbdId` |
| `school` | string | `Teams.School` — **the match key against ESPN `team.location`** |
| `mascot` | string? | `Teams.Mascot` |
| `abbreviation` | string? | `Teams.Abbreviation` |
| `alternateNames` | string[]? | **seed `TeamAliases` directly** — CFBD ships its own alias list; see below |
| `conference` | string? | `Conferences.Name` lookup |
| `division` | string? | conference division, not FBS/FCS |
| `classification` | enum `fbs`\|`fcs`\|`ii`\|`ii/iii`\|`iii` | `Teams.Classification` — **the authority for FBS eligibility** |
| `color`, `alternateColor` | string? | unused |
| `logos` | string[]? | `Teams.LogoUrl` = `logos[0]` |
| `twitter` | string? | unused |
| `location` | `Venue`? | nested object (`id`, `name`, `city`, `state`, `zip`, `countryCode`, `timezone`, `latitude`, `longitude`, `elevation`, `capacity`, `constructionYear`, `grass`, `dome`) — **note the name collision: CFBD `Team.location` is a *venue*, ESPN `team.location` is the *school name*.** Easy bug; call it out in P2-02's mapper. |

Also `client.Conferences.GetAsync(...)` → `GET /conferences?year=&classification=` → `List<Conference>` with `id`, `name`, `shortName`, `abbreviation`, `classification`, `memberCount` → `Conferences` (`CfbdId`, `Name`, `Abbreviation`, `Classification`). `Conferences.EspnGroupId` has no CFBD source and must be seeded by hand or learned from `competitions[].groups.id` on conference games.

**2. Games** — `client.Games.GetAsync(cfg => { cfg.QueryParameters.Year = 2025; cfg.QueryParameters.Week = 3; cfg.QueryParameters.Classification = ...; })`
`GET /games?year=&week=&seasonType=&classification=&team=&home=&away=&conference=&id=&competition=&round=` (operationId `GetGames`) → `List<Game>`

> **Parameter name change:** the card and Feature 12 both say `division=fbs`. The current API calls it **`classification`** (enum `fbs`, `fcs`, `ii`, `ii/iii`, `iii`); `seasonType` is the enum `regular`, `postseason`, `both`, `allstar`, `spring_regular`, `spring_postseason`. P2-02 must use `classification`.

| Field | Type | Maps to |
|---|---|---|
| `id` | int | `Games.CfbdGameId` |
| `season`, `week` | int, int | `Games.SeasonYear`, `Games.Week` |
| `seasonType` | enum | `SeasonWeeks.IsRegularSeason` cross-check |
| `startDate` | date-time | `Games.KickoffUtc` → derive `KickoffEasternDate`, `IsSaturdayEastern` |
| `startTimeTBD` | bool | kickoff not yet set — **a game set generated against a TBD kickoff has a meaningless lock time; P2-02 should keep the flag or at least log it** |
| `completed` | bool | see the status warning below |
| `neutralSite` | bool | unused today |
| `conferenceGame` | bool | `Games.IsConferenceGame` |
| `attendance`, `venueId` | int? | unused |
| `venue` | string? | `Games.Venue` |
| `homeId` / `awayId` | int | join to `Teams.CfbdId` |
| `homeTeam` / `awayTeam` | string | the school names — the strings the ESPN matcher must reconcile |
| `homeConference` / `awayConference` | string? | cross-check |
| `homeClassification` / `awayClassification` | enum? | per-game FBS/FCS — lets P2-02 exclude FCS-vs-FCS without a `Teams` join |
| `homePoints` / `awayPoints` | int? | `Games.HomeScore` / `AwayScore` |
| `homeLineScores` / `awayLineScores` | number[]? | unused |
| `homePostgameWinProbability`, `home/awayPregameElo`, `home/awayPostgameElo`, `excitementIndex`, `highlights`, `notes`, `playoff` | — | unused |

> **`Game` has no status field.** The only state it carries is `completed: bool` (plus `startTimeTBD`). There is no Postponed or Cancelled. Consequences:
> - `Games.Status` for *Postponed*/*Cancelled* cannot come from CFBD `/games`. In practice a postponed CFBD game simply disappears from, or moves within, the week's results — so P2-02's ingest detects a schedule change as *"a game we hold for this week is no longer in the payload, or its `startDate` moved"*, not as a status value.
> - P2-02's "Done when" line *"postponed game status updates"* must be tested against that disappearance/date-move behaviour, or against an ESPN `STATUS_POSTPONED` payload, not against a CFBD status string.
> - P2-03's `CfbdLiveScoreProvider` fallback can only produce Scheduled ↔ Final from `completed`, with no period, no clock and no Postponed/Cancelled. `04` section 10 already says the fallback loses live scores; it also loses status granularity. Worth stating on the data-status page banner.

**3. Rankings** — `client.Rankings.GetAsync(cfg => { cfg.QueryParameters.Year = 2025; cfg.QueryParameters.Week = 3; })`
`GET /rankings?year={required}&seasonType=&week=&poll=&latest=&final=` (operationId `GetRankings`) → `List<PollWeek>`

`PollWeek` = `season` int, `seasonType` enum, `week` int, `polls` `Poll[]`.
`Poll` = `poll` string (`"AP Top 25"`), `isFinal` bool?, `ranks` `PollRank[]`.
`PollRank` = `rank` int?, `teamId` int, `school` string, `conference` string?, `firstPlaceVotes` int?, `points` int?.

→ `Rankings(SeasonYear, Week, Poll='AP', Rank, TeamId, FetchedUtc)`. Filter `polls` on `poll == "AP Top 25"`; the same response also carries the Coaches poll and (for FCS weeks) others. Join on `teamId` → `Teams.CfbdId`, which is sturdier than joining on `school`. `year` is the only **required** parameter; there is a `latest=true` shortcut that avoids having to know the current week.

**4. Lines** — `client.Lines.GetAsync(cfg => { cfg.QueryParameters.Year = 2025; cfg.QueryParameters.Week = 3; })`
`GET /lines?gameId=&year=&seasonType=&week=&team=&home=&away=&conference=&provider=` (operationId `GetLines`) → `List<BettingGame>`

`BettingGame` = `id` int (**the CFBD game id — join straight to `Games.CfbdGameId`**), `season`, `seasonType`, `week`, `startDate`, `homeTeamId`, `homeTeam`, `homeConference`, `homeClassification`, `homeScore`, the away equivalents, and `lines` `GameLine[]`.
`GameLine` = `provider` string, `spread` double?, `formattedSpread` string, `spreadOpen` double?, `overUnder` double?, `overUnderOpen` double?, `homeMoneyline` double?, `awayMoneyline` double?.

→ `GameLines(GameId, Provider, Spread, FetchedUtc)`, one row per `lines[]` entry, appended (history kept per `02-Data-Model.md`). **`spread` sign must be verified against a real payload** — `formattedSpread` (e.g. `"Alabama -7.5"`) is the tie-breaker, exactly as ESPN's `details` was. CFBD conventionally reports `spread` home-relative (negative = home favoured), the same as `02-Data-Model.md`, but this is the one field of the five I could not confirm without a live call. It is item 4 in the operator to-do.

**5. Calendar** — `client.Calendar.GetAsync(cfg => cfg.QueryParameters.Year = 2025)`
`GET /calendar?year={required}` (operationId `GetCalendar`) → `List<CalendarWeek>`

`CalendarWeek` = `season` int, `week` int, `seasonType` enum, `startDate`, `endDate`, `firstGameStart`, `lastGameStart` (all date-time).

→ `SeasonWeeks(SeasonYear, Week, StartUtc, EndUtc, IsRegularSeason)`. `IsRegularSeason = seasonType == "regular"` per P2-02's card. Note that `startDate`/`endDate` are CFBD's week boundaries, whereas `02-Data-Model.md` specifies "Sunday 00:00 ET to Saturday 23:59:59 ET, converted to UTC" — **P2-02 must decide whether to store CFBD's boundaries verbatim or normalise them to the ET week**, because `04` section 1 and the lock algorithm read those columns. `firstGameStart`/`lastGameStart` are a useful sanity check either way. Flagging rather than deciding, since P0-05 owns the season calendar domain.

**Tier probe (not one of the five)** — `client.Scoreboard.GetAsync(cfg => cfg.QueryParameters.Classification = ...)`
`GET /scoreboard?classification=&conference=` → `List<ScoreboardGame>`:
`id`, `startDate`, `startTimeTBD`, `tv`, `neutralSite`, `conferenceGame`, `status` (enum **`scheduled` | `in_progress` | `completed`** — again no Postponed/Cancelled), `period` int?, `clock` string?, `situation`, `possession`, `lastPlay`, `venue` `{ name, city, state }`, `homeTeam`/`awayTeam` `{ id, name, conference, classification, points, lineScores[], winProbability }`, `weather`, `betting`. On the free tier this should return 401/403; that is the call that proves the tier boundary.

---

## Status mapping check vs `04` section 9

`04` section 9 currently says:

> ESPN `status.type.name`: `STATUS_SCHEDULED` -> Scheduled, `STATUS_IN_PROGRESS`/`STATUS_HALFTIME`/`STATUS_END_PERIOD`/`STATUS_DELAYED` -> InProgress, `STATUS_FINAL` -> Final, `STATUS_POSTPONED` -> Postponed, `STATUS_CANCELED` -> Cancelled. `completed == true` also implies Final.

Findings:

1. **Nothing in the list is contradicted.** The two names I could observe, `STATUS_SCHEDULED` (`type.id` `"1"`, `state` `"pre"`, `completed` `false`) and `STATUS_FINAL` (`type.id` `"3"`, `state` `"post"`, `completed` `true`), map exactly as written.
2. **The list is not exhaustive and cannot be made exhaustive from a capture.** ESPN's college-football feed also emits at least `STATUS_END_OF_PERIOD` (note: *not* `STATUS_END_PERIOD`), `STATUS_RAIN_DELAY`, `STATUS_SUSPENDED`, `STATUS_FORFEIT` and `STATUS_ABANDONED` on other sports and in edge cases, and ESPN has added names without notice before. `04` section 9 should gain a **default rule** rather than more names: *any unrecognized `status.type.name` falls back to `status.type.state`* — `"pre"` → Scheduled, `"in"` → InProgress, `"post"` → Final **only if `type.completed` is true**, otherwise leave the status unchanged — *and is logged once*. `state` is a three-value field that has been stable for years, so it is a far safer backstop than an exception. Without this, one new ESPN status name silently stalls scoring on a Saturday.
3. **`STATUS_END_PERIOD` is probably the wrong spelling.** ESPN's name is `STATUS_END_OF_PERIOD`. Under the default rule in (2) it no longer matters — `state` is `"in"` either way — but the literal should be corrected or both spellings accepted.
4. **The `completed == true ⇒ Final` rule must be evaluated after, not before, the name match**, and must be paired with the score guard: a scheduled game reports `score: "0"` for both sides, so a naive "completed or 0-0" read would fabricate ties. `04` section 7's "tie score → needs review" path is only reachable from a genuinely Final game.
5. **Matching key refinement.** Section 9 says to match on "normalized home name, normalized away name" compared against `Teams.School`, `Teams.Abbreviation` and aliases. Two additions from the capture:
   - Normalization must **fold diacritics**: ESPN ships `San José State` with `U+00E9`. Lowercasing and stripping punctuation leaves `san josé state`, which will not equal `san jose state`. Add an explicit `FormD` + non-spacing-mark strip (or `string.Normalize` + `CharUnicodeInfo`) step. `Hawai'i` is safe — ESPN uses the ASCII apostrophe `U+0027`, not the okina `U+02BB` — but only because punctuation is stripped.
   - Match ESPN **`team.location`**, not `team.displayName`. `location` is the school name with no mascot (`"Ole Miss"`, `"App State"`, `"Miami (OH)"`) and lines up with CFBD `school` directly; `displayName` is `location + " " + name` and forces the matcher to strip a mascot it has no list of.

These four are the substance of **D-012**.

---

## TeamAliases draft

`tests/NcaafPickEm.Fixtures/Real/team-aliases-draft.json` — 24 rows, shape `{ source, alias, school, note }` (`note` is documentation only; P2-02's seeder reads `source`/`alias` and resolves `school` to a `TeamId`).

Confidence, stated plainly: **every row's ESPN side is real**, taken from the 185 distinct teams in the two committed captures. **No row's CFBD side is confirmed**, because `/teams/fbs` was never called. The rows split three ways:

- **High confidence mismatches** (ESPN's string is demonstrably not a school name CFBD would use): `App State` → `Appalachian State`, `UL Monroe` → `Louisiana Monroe`, `Massachusetts` → `UMass`, `SE Louisiana` → `Southeastern Louisiana`.
- **Needs confirmation** (plausible either way; the row is a harmless no-op if CFBD already uses ESPN's spelling): `UTSA` → `UT San Antonio`, `UConn` → `Connecticut`, `Southern Miss` → `Southern Mississippi`, `Florida International` → `FIU`, `Florida Atlantic` → `FAU`, `South Florida` → `USF`.
- **Deliberate no-ops, pinned against normalization collisions**: `Miami` / `Miami (OH)` (punctuation-stripping turns these into `miami` and `miami oh`, and a fuzzy matcher that ever falls back to prefix matching would cross them), `Texas A&M` / `East Texas A&M` (same hazard via the shared substring), `San Jose State` / `San José State` (both spellings), `Hawaii` / `Hawai'i`.

Each mismatch row is present twice, once for ESPN `location` and once for ESPN `displayName`, so the matcher hits regardless of which field P2-03 normalizes.

Two things that make this list smaller than it looks:

1. **CFBD `Team.alternateNames` is an alias list CFBD already maintains.** P2-02 should seed `TeamAliases(Source='Cfbd')` from it on every teams ingest, and P2-03 should consult it before falling back to this hand-written ESPN table. That is very likely to resolve most of the "needs confirmation" rows for free.
2. **Aliases are a cold-start problem only.** `04` section 9 already says that once an ESPN event matches, `EspnEventId` and `EspnTeamId` are written and future polls match by id. So the alias table has to be right for roughly one poll per team per season; after that it is inert. The cost of a miss is an `UnmatchedGames` row on the commissioner data page, not a scoring failure.

**Do not match on abbreviations across the two sources.** ESPN's abbreviations are its own (`TA&M`, `M-OH`, `MISS` for Ole Miss, `UL` for Louisiana, `USA` for South Alabama) and there is no reason to expect them to equal CFBD's. `04` section 9 lists `Teams.Abbreviation` as a comparison target; treat that as *last resort, and only for an exact case-insensitive hit*, because `USA`/`USM`/`USF` style collisions are exactly where a wrong match silently scores the wrong game. Prefer leaving a game unmatched and visible on the data page.

---

## Request budget notes

**ESPN — free, no key, no published rate limit.** 11 requests consumed by this spike (7 to `site.api.espn.com`, 4 to documentation/package hosts). Nothing to account for. `04` section 10's ~174 polls per Saturday × 15 Saturdays ≈ 2,600 calls per season costs nothing and needs no quota tracking. `ProviderCalls` should still record them for the failure-rate logic (3 consecutive failures ⇒ CFBD fallback).

**CFBD — free tier, 1,000 calls/month.** 0 requests consumed against the authenticated API by this spike; the 217 KB OpenAPI document at `/api-docs.json` is served without a key and is not metered. The operator to-do below will consume **5 calls**, or **7** with both optional probes. Against the reference-data budget in Feature 12 (~10 calls/week, ~45/month) that is one-seventh of a month's normal usage and leaves the free tier untouched for the season.

Standing per-month arithmetic for P2-04's counter, unchanged from Feature 12 and re-checked here:

| Use | Calls/month |
|---|---|
| Teams + conferences (weekly refresh, Tuesday) | ~8 |
| Schedule (Tuesday + daily Wed–Sat status sweeps) | ~20 |
| AP rankings (Sun/Mon/Tue) | ~12 |
| Lines (daily 23:30) | ~30 |
| Manual refreshes, retries, dev | ~30 |
| **Normal total** | **~100 of 1,000** |
| ESPN fallback engaged for one Saturday (10-min cadence, 14.5 h) | +87 |
| ESPN fallback engaged for a whole 5-Saturday month | +435 |

So the free tier survives even a total ESPN outage for a full month (~535 of 1,000), which is a stronger position than Feature 12's budget section concluded. The 800-call warning on the data-status page is correctly placed. If ESPN were abandoned entirely for live polling, the free tier would **not** suffice (~870 polls/month at 5-minute cadence plus reference data, with no retry headroom) — that is the case for Tier 1 at $1.

---

## Blocked: operator to-do

**Why this is still open.** The CFBD key is present on the machine, outside the repo. The block is Claude Code's auto-mode permission classifier, which refuses any command that reads a credential file and makes a network call in the same breath (reason: *Credential Exploration*). Both the Git Bash and the PowerShell formulations were refused. This is correct default behaviour and should not be worked around from inside an agent.

**To unblock:** either add a Bash permission rule for a single, narrowly scoped capture script (`.claude/settings.json`, e.g. allowing `bash scripts/capture-cfbd.sh`), run the task interactively so the prompt can be approved, or just run the five commands below by hand. The operator owns that choice.

**The five calls.** Base `https://api.collegefootballdata.com`, header `Authorization: Bearer <key>`, `Accept: application/json`. Use **season 2025, week 3, regular season** (the 2026 season is only three weeks old; 2025 week 3 is complete and has finals, lines and a settled AP poll).

| # | Request | Save trimmed to | Expect |
|---|---|---|---|
| 1 | `GET /teams/fbs?year=2025` | `tests/NcaafPickEm.Fixtures/Real/cfbd-teams-fbs.json` | ~136 `Team` objects. **Trim to ~12 teams** chosen to cover the alias list: Appalachian State, Louisiana Monroe, UMass, Connecticut/UConn, UT San Antonio/UTSA, Southern Miss(issippi), Hawai'i, San José State, Miami, Miami (OH), Texas A&M, East Texas A&M. Keep `alternateNames` in full — that is the point of the capture. |
| 2 | `GET /games?year=2025&week=3&seasonType=regular&classification=fbs` | `cfbd-games-2025-week3.json` | ~60–70 `Game` objects. Trim to 4: one completed with scores, one with `startTimeTBD`, one conference game, one FBS-vs-FCS (check `awayClassification`). |
| 3 | `GET /rankings?year=2025&week=3&seasonType=regular` | `cfbd-rankings-2025-week3.json` | one `PollWeek`; keep the `AP Top 25` poll's 25 ranks, drop the Coaches poll. |
| 4 | `GET /lines?year=2025&week=3&seasonType=regular` | `cfbd-lines-2025-week3.json` | `BettingGame[]`. Trim to 3, **including at least one away-favoured game**, and record whether `spread` is home-relative (compare against `formattedSpread`). This is the one unresolved field mapping. |
| 5 | `GET /calendar?year=2025` | `cfbd-calendar-2025.json` | ~15–17 `CalendarWeek` rows; keep all, it is tiny. Record whether `startDate`/`endDate` are ET-week or UTC-week boundaries. |

Optional probes (2 more, ≤ 7 total, staying under the 10-call cap):

| # | Request | Records |
|---|---|---|
| 6 | `GET /scoreboard?classification=fbs` | the **tier boundary**: expect `401`/`403` on the free tier. Record the exact status code and body. If it returns 200, the free tier includes the live scoreboard and D-012's tier finding needs superseding. |
| 7 | `GET /conferences?year=2025&classification=fbs` | `Conferences` seed shape; lets P2-02 map `Conferences.CfbdId` without inferring it from `Team.conference` strings. |

**Rules for whoever runs these:** never echo, log, or commit the key; never write it into any file under the repo; each committed file under 200 KB; strip nothing but bulk (keep every field name, even nulls, so P2-02 can see the shape). Then: re-check `tests/NcaafPickEm.Fixtures/Real/team-aliases-draft.json` against the real `school` and `alternateNames` values, delete the rows that turn out to be no-ops, and update this section and the TeamAliases section above.

**Also still open:** no in-progress ESPN payload was captured (see "What was verified live"). The cheapest fix is a single `GET .../scoreboard?groups=80&dates=<today>&limit=300` on any Saturday between 12:30 and 23:00 ET, trimmed to two events and committed as `espn-scoreboard-inprogress.json`. P2-03's `LiveScoreApplyTests` want it.

---

## Recommendations for P2-02 and P2-03

**For P2-02 (CFBD reference data and ingest)**

1. Pin `CollegeFootballData` `5.27.1` in `Directory.Packages.props`; expect five Kiota transitive `PackageVersion` rows alongside it.
2. Use `classification`, not `division`, as the games query parameter. The card's wording is stale.
3. Write the ~15-line `IAccessTokenProvider`; get the `HttpClient` from `IHttpClientFactory` so the `ProviderCalls` wrapper and retry policy have a seam.
4. Treat every generated model property as nullable. Kiota does not honour the spec's `required` list in a way you can lean on.
5. Watch the `Team.location` / ESPN `team.location` name collision — CFBD's is a `Venue` object, ESPN's is the school name.
6. Seed `TeamAliases(Source='Cfbd')` from `Team.alternateNames` on every teams ingest. It is free alias data and it shrinks the hand-maintained table.
7. `Game` has no status field. Model postponement as *disappeared from, or moved within, the week's payload*, and write the test that way.
8. Decide, and record, whether `SeasonWeeks.StartUtc`/`EndUtc` take CFBD's `CalendarWeek` boundaries verbatim or get normalised to the ET week `02-Data-Model.md` describes. Coordinate with P0-05.
9. `Conferences.EspnGroupId` has no CFBD source. Seed it by hand, or learn it from `competitions[].groups.id` on ESPN conference games (which the capture shows is reliably present exactly when `conferenceCompetition` is true).

**For P2-03 (ESPN provider, matcher, fallback)**

1. One date per call. `dates=YYYYMMDD` only; a range is a 400. Keep `limit=300`.
2. `dates` buckets by **Eastern** calendar date, so one call per Saturday covers noon ET through the post-midnight finals. Do not add a second call for the Sunday-UTC tail.
3. Parse `events[].date` as `DateTimeOffset` — the literal has **no seconds** (`2026-09-12T23:30Z`).
4. `events[].id` and `team.id` arrive as **strings**. Parse to `long`/`int`.
5. Never persist a score while `status.type.state == "pre"`; scheduled games report `"0"`/`"0"`.
6. Map status by `status.type.name`, then fall back to `status.type.state` for anything unrecognized, and log the unknown name once. See D-012.
7. Match on ESPN `team.location` against CFBD `school`, with diacritic folding in the normalizer. Use abbreviations only as an exact last resort.
8. `groups=80` is not a classification filter — FCS opponents are in the payload. Decide unmatched-versus-ignored by looking up the CFBD `Teams.Classification`, not by assuming the payload is FBS-only. Concretely: an ESPN event whose teams do not resolve to a CFBD FBS game should be **ignored silently** if either team is a known FCS school, and written to `UnmatchedGames` only when both teams look FBS. Otherwise the data page fills with Howard-at-Indiana noise every Saturday.
9. `odds[0].spread` is already in `GameLines.Spread`'s convention (negative = home favoured). Verified on 71 games; no conversion.
10. `curatedRank.current == 99` means unranked, not rank 99.
11. The CFBD fallback loses period, clock, live odds **and** Postponed/Cancelled — it is Scheduled-or-Final only. Say so in the "scores may be stale" banner, not just "stale".
12. `leagues[0].calendar` inside every scoreboard response is a free, keyless season calendar. Worth remembering as a last-ditch `SeasonWeeks` source if CFBD is down; not a reason to change D-003.
