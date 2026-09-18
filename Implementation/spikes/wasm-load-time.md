# Spike: Blazor WebAssembly load time (P0-04)

Question from `01-Architecture.md` ("Load-time risk on Blazor WASM") and Feature 04: is the picks
page interactive within 2 s on a repeat load, and is the first load under 5 s? If not, the fallback
is Razor Pages plus small vanilla JS modules, decided before any Phase 1 UI work.

Status: **measured locally, PASS with margin. Phone measurement over Tailscale is still pending the
operator** (agents cannot use an iPhone or the tailnet). See "Operator to-do".

## Method

- Build: `dotnet publish src/NcaafPickEm.Api -c Release` (the hosted app: Api serves the Web
  project's `_framework` payload through `MapStaticAssets`, per D-011). `PublishTrimmed=true`,
  `InvariantGlobalization=true` (D-012), Brotli + gzip precompression on.
- Server: the published output run with `ASPNETCORE_ENVIRONMENT=Production` on
  `http://localhost:5215` on the development machine (Windows 11).
- Client: Microsoft Edge 153 headless, driven over the DevTools Protocol from PowerShell.
  Device metrics emulated at 375 x 812, DPR 2.
- "Render" = wall clock from `Page.navigate` until the Blazor router has rendered the home page's
  `<h1>` into the DOM, polled every 25 ms. That is later than first paint (the branded splash
  paints in the first few tens of ms) and is the number that matters for "interactive".
- Bytes = `performance.getEntriesByType('resource')` `transferSize` sum, i.e. what actually crossed
  the wire with content negotiation applied (Brotli).
- Cold = `Network.clearBrowserCache` before navigating. Note that clearing the HTTP cache does not
  clear the *service worker* cache, so only the very first run of a fresh browser profile is a true
  cold load; the later "cold" rows below show what a repeat visit looks like once the service
  worker has installed.

## Published payload

| Bucket | Files | Raw | Brotli (served) |
|---|---|---|---|
| `_framework` (runtime + assemblies) | 41 | 6.35 MB | **2.07 MB** |
| App shell (index.html, app.css, icons, manifest, service worker) | 10 | 27 KB | ~15 KB |
| **Total first load, measured on the wire** | 46 requests | - | **2.19 MB** |

Largest items (Brotli / raw):

| File | Brotli | Raw |
|---|---|---|
| `dotnet.native.*.wasm` (runtime) | 954 KB | 2932 KB |
| `System.Private.CoreLib.*.wasm` | 482 KB | 1551 KB |
| `System.Text.Json.*.wasm` | 122 KB | 364 KB |
| `Microsoft.AspNetCore.Components.*.wasm` | 90 KB | 251 KB |
| `dotnet.runtime.*.js` | 47 KB | 194 KB |

No ICU data ships at all (`InvariantGlobalization=true`, D-012); that alone removed roughly 1.5 MB
of raw payload. AOT is not available on this machine (no `wasm-tools` workload, `AGENT-NOTES.md`)
and is not needed at this size.

## Measured locally (Edge headless, 375 x 812, localhost)

| Run | Time to rendered page | Requests | Bytes over the wire |
|---|---|---|---|
| Cold 1 (empty cache, service worker not yet installed) | **507 ms** | 46 | 2,192,385 |
| Cold 2 (HTTP cache cleared, service worker installed) | 321 ms | 46 | 0 |
| Cold 3 (same) | 334 ms | 46 | 0 |
| Repeat 1 (warm) | 310 ms | 45 | 0 |
| Repeat 2 (warm) | 291 ms | 45 | 0 |
| Repeat 3 (warm) | 322 ms | 45 | 0 |

Localhost has no network cost, so ~300 ms is the floor: runtime instantiation plus first render on
a desktop CPU. Everything above that on a phone is transfer time plus the phone's slower WASM
instantiation.

## Estimated on an iPhone over Tailscale

Transfer estimate = 2.19 MB = 17.5 Mbit, plus ~6 round trips of connection and request latency, plus
iPhone runtime instantiation (roughly 2-3x the desktop figure, so ~0.6-1.0 s).

| Path | Bandwidth / RTT | Transfer | Estimated first load | Estimated repeat load |
|---|---|---|---|---|
| Home Wi-Fi, Tailscale direct | 50 Mbps / 5 ms | ~0.35 s | **~1.0-1.5 s** | ~0.6-1.0 s |
| LTE, Tailscale direct | 10 Mbps / 50 ms | ~1.75 s | **~2.6-3.2 s** | ~0.6-1.0 s |
| LTE, Tailscale DERP relay | 5 Mbps / 120 ms | ~3.5 s | **~4.5-5.0 s** (at the limit) | ~0.6-1.0 s |

Repeat loads do not touch the network at all: the service worker serves the whole shell from its
cache (proved by the 0-byte rows above), so the repeat-load number is CPU only and is far inside the
2 s budget. The only scenario near the 5 s first-load threshold is a first install over a relayed
cellular connection - a once-per-device event, behind a branded splash with a progress ring.

**Conclusion: proceed with Blazor WASM. D-001 stands** (logged as D-014). The fallback trigger is
unchanged and stays open until the operator's phone measurement below is recorded.

## What this spike also fixed

1. The hosted app did not boot at all before this task. `index.html` shipped the SDK's literal
   `#[.{fingerprint}]` placeholder because the placeholder rewrite only runs when the Blazor project
   is published on its own, never when the Api publishes it as a hosted reference - so
   `/_framework/blazor.webassembly#[.{fingerprint}].js` 404'd while every other `/_framework/*` URL
   returned 200 (which is why P0-01's curl-only smoke test passed). Fixed by referencing the stable
   `_framework/blazor.webassembly.js` path and turning the rewrite off (D-013).
2. `TrimMode=full` broke component activation: the router fell through to `NotFound` and threw
   `CtorNotLocated` because the trimmer had removed component constructors reached only by
   reflection. Reverted to the SDK default trim mode; the payload difference was negligible.

Both faults are invisible to `curl` and to `dotnet test`. Any future change to trimming, publishing,
or `index.html` must be re-verified by actually rendering the app in a browser.

## Operator to-do (the manual half of this spike)

Agents cannot use an iPhone or the tailnet. Please do this before Phase 1 UI work is signed off and
paste the numbers into the table at the end of this file.

1. On the home server (or a laptop on the tailnet):
   `dotnet publish src/NcaafPickEm.Api -c Release -o C:\pickem-spike`
   then `cd C:\pickem-spike` and
   `dotnet NcaafPickEm.Api.dll --urls https://0.0.0.0:5001` with a Tailscale cert
   (`tailscale cert <machine>.<tailnet>.ts.net`), or run behind the existing HTTPS setup. HTTPS is
   required: iOS will not install a PWA or register a service worker over plain HTTP.
2. On the iPhone, on cellular (not Wi-Fi), open `https://<machine>.<tailnet>.ts.net:5001/` in
   Safari. Time from tap to the "Welcome" screen. That is **first load**.
3. Share -> **Add to Home Screen**. Confirm the icon and the name "Pick Em".
4. Open the app from the home screen. Confirm: no Safari chrome (standalone), the navy top bar runs
   under the status bar, the tab bar sits above the home indicator. Time from tap to content: that
   is **repeat load**.
5. Kill the app, turn on Airplane mode, open it again: the shell should still paint (service worker
   cache) even though data calls fail.
6. Optional but useful: Safari -> Develop -> iPhone -> Web Inspector, Network tab, to get the real
   transferred byte count over the tailnet.

Thresholds: first load > 5 s or repeat load > 2 s means stop and escalate to the orchestrator for
the Razor Pages fallback decision, before Phase 1 UI starts.

| Measurement | Value | Date | By |
|---|---|---|---|
| iPhone first load (Safari, cellular) | _pending_ | | |
| iPhone repeat load (home-screen app) | _pending_ | | |
| Installs to home screen, opens standalone | _pending_ | | |
| Shell still paints offline | _pending_ | | |
