# Internet Observatory — Native Avalonia Implementation Plan

> **STATUS: PLANNED — NO APPLICATION CODE IMPLEMENTED**
>
> **Created:** 2026-10-24  
> **Repository:** ben0kay/BEN_TOOLBOX_2.0 / main  
> **Audience:** Codex / other implementation assistants  
> **Area:** Internet-powered desktop monitoring / non-game toolbox  
> **Proposed product:** **INTERNET OBSERVATORY**, a standalone native Avalonia tool, not a redesign of the primary dashboard.
>
> **Implementation policy:** Read the current code and root README before editing; implement **only the phases assigned by the user**, update documentation with accurate build/test evidence and make one scoped commit per job. Don't claim that any phase is done simply because this plan exists.

## 1. Vision and scope

Build a visually compelling **internet-powered monitoring command center** inside BEN TOOLBOX 2.0. It should display *real, sourced* information about the Earth and selected internet resources, with a central map, small status cards, interactive filters and contextual details.

Initial sources:
1. **Global Earthquake Monitor:** USGS GeoJSON event feeds, magnitude/depth/time/location, map pins and detail panel.
2. **Space Weather Monitor:** NOAA SWPC planetary Kp and aurora-related forecast data, recent values/trends and source timestamp. Advanced map overlays later.
3. **Website Change & Availability Monitor:** user-added public sites, HTTP status/response time and compact last-change snapshots.
4. **Network Health:** user-selected connectivity targets, response-time chart and meaningful online/timeout/error states.

This is **not a surveillance product** or a "real-time global internet radar" based on fake data. Every displayed number/pin must be backed by an actual source response or visibly labelled mock/offline/test data. "Live" means *recently fetched*, subject to provider reporting and network delay—not instantaneous worldwide coverage.

**Design priority:** A functional, readable **dark navy/cyan sci-fi UI** using Avalonia controls, real data, map interaction and graceful offline behavior. A vivid command-centre aesthetic must not sacrifice clarity, responsiveness or truthfulness.

**Cost:** Start with public no-paid-key datasets where permitted. HTTP bandwidth, optional advanced map/monitor providers, and data licensing may impose restrictions; avoid surprising charges and do not claim perpetual free/unlimited service.

## 2. Existing BEN TOOLBOX architecture — inspect before implementing

- \`README.md\` describes .NET 10, Avalonia 12, the native toolbox design and module categories.
- \`Services/ToolLauncher.cs\`: explicit allowlisted \`ToolCatalog\`, native tool statuses and categories.
- \`Views/MainWindow.axaml.cs\`: \`LaunchTool\` branches for native Avalonia tool windows, four top-level navigation sections.
- \`Views/MainWindow.Dashboard.cs\`, \`Views/Dashboard/Controls/TelemetrySparkline.cs\`, \`Services/SystemTelemetryService.cs\`, \`Services/TelemetryHistory.cs\`: existing local CPU/RAM/drive dashboard, bounded sparklines and visible-only sampling. Reuse design patterns, not telemetry data ownership.
- \`Docs/DashboardV2ImplementationPlan.md\`: dashboard visual/performance guidance. **Do not absorb Observatory into that Dashboard or conflict with its ongoing phases.**
- Existing image/sprite, file and YouTube utilities should remain untouched. No new SDK requirement except libraries explicitly justified by the chosen implementation.
- Use \`HttpClient\`, \`System.Text.Json\`, cancellation, bounded caches, deterministic state records and existing Avalonia primitives first. Avoid a framework of dozens of abstractions for four feeds.

**Navigation recommendation (first version):** Register a standalone native tool with id \`internet-observatory\` and category \`Inspect\` so the current four top-level sections do not need an invasive redesign. Use an explicit launch branch and accurate "NATIVE AVALONIA / INTERNET DATA" status. "Project Inspectors" is an imperfect label for this general-purpose monitor; document that as a temporary placement and reconsider a dedicated **Internet** category/nav only if the user wants multiple internet tools. Do not introduce a new top-level sidebar button in O0–O4 unless requested.

## 3. Data source contracts — official references

### A. Earthquakes — USGS (first actual internet feed)

Official:
- https://earthquake.usgs.gov/earthquakes/feed/v1.0/geojson.php
- Example compact public feed: https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/2.5_day.geojson
- Alternatives: \`all_day.geojson\`, \`4.5_week.geojson\`, \`significant_month.geojson\`, choose feed based on selected range/threshold; do not fetch every feed simultaneously.

USGS GeoJSON uses \`features[]\`, \`id\`, \`properties.mag/place/time/updated/url\`, \`geometry.coordinates = [longitude, latitude, depth]\`. Check missing/invalid values and units. Parse event timestamp in milliseconds since Unix epoch **to UTC** and show local/UTC preference explicitly.

USGS short-window GeoJSON feeds are normally updated about once per minute; schedule a **default 2-minute refresh while active**, with manual Refresh. This does not guarantee event detection instantly. Deduplicate by stable USGS ID; differentiate newly reported vs updated events. Support filtering locally by minimum magnitude, selected time period and visible map bounds without new HTTP calls for each slider adjustment. IO2 includes named presets: Past 24 hours, Past week, and Significant events. Select one appropriate allowlisted USGS feed per preset (for example 2.5_day, 4.5_week, significant_month); inspect actual feeds at implementation time. Display the exact feed, covered period and magnitude coverage beside counts: a thresholded feed is not a count of all earthquakes. Preset changes may fetch their corresponding feed; ordinary local filters must not trigger repeated network calls.

Required details: magnitude (when supplied), location label, depth in km, event time, report/update time, tsunami flag if published, and link to the original USGS event page. Not every data field is guaranteed. Never label earthquakes as active hazards or issue safety alerts without authoritative context.

### B. Space weather — NOAA SWPC (after earthquake MVP)

Official service page:
- https://www.spaceweather.gov/content/data-access
- Planetary K index product: https://services.swpc.noaa.gov/products/noaa-planetary-k-index.json
- SWPC scales: https://services.swpc.noaa.gov/products/noaa-scales.json
- Aurora forecast grid: https://services.swpc.noaa.gov/json/ovation_aurora_latest.json
- NOAA product overview: https://www.spaceweather.gov/products/aurora-30-minute-forecast

**Important API stability:** NOAA JSON fields/columns can change. For example, NOAA announced 2026 JSON format changes affecting K-index and other products. Codex must inspect **the current actual endpoint payload and authoritative product docs** at implementation time, validate shape and handle changes/errors without crashing.

First NOAA module should show: Kp history/current reported value and timestamp, interpretation with grounded scale, separate provider-updated time and fetch time, and official source link. An optional aurora indicator/grid visualization must clearly label its forecast lead time and avoid translating forecast intensity into a guaranteed local sighting.

**Do not draw NOAA aurora latitude grid as "earthquake pins".** It is a different data geometry and forecast product; begin with a dedicated card/chart, then optional dedicated overlay/aurora view after validating projection and interpolation.

**Refresh:** start with **5-minute polling only while module visible/active**; respect provider updates and HTTP cache headers. Large grid feed may be ~1 MB or more, so don't download it every refresh if unused.

### C. Website monitor — user-chosen targets (later)

- HTTP(S) websites user explicitly adds. Default minimum cadence e.g. **5 minutes**, plus manual check.
- Record URL, host label, HTTP response code if available, effective URL (after safe redirects), measured HTTP timing, previous successful content fingerprint, change/no-change/unknown state, last successful check timestamp and last failure.
- Support optional compact text snapshot/diff of *public non-authenticated HTML* only; dynamic pages may produce false positives. Normalize content conservatively; show raw source/what was ignored; do not claim perfect semantic change detection.
- Separate "site responded" from "page changed". A 403/429, CAPTCHA, login page, script-rendered content or anti-bot block must not be presented as "website offline" or "content unchanged".
- Never follow private intranet/router/loopback/metadata endpoint URLs by default; guard against redirects to private IP ranges, DNS rebinding and unusual ports. Prohibit credentials in saved URLs and avoid storing cookies, tokens or personal browsing/session data. Users can choose explicit advanced LAN diagnostics in a separate reviewed phase, not V1.
- Respect \`robots.txt\`/site terms where relevant, caching/Retry-After and per-host request limits. This is light monitoring, **not an unrestricted scraper**.

### D. Network health — user-chosen simple targets (later)

- User-controlled target list, sensible public default(s) that provide a stable lightweight HTTPS health endpoint or DNS resolution target; choose a trusted one with permission and document.
- Begin with timed HTTP GET/HEAD or DNS resolution, rather than assuming Windows ICMP ping is always allowed. Show which probe type is in use; treat HEAD=405 as unsupported, not total internet outage. ICMP can be optional.
- Chart measured latency (ms) and success/failure over rolling history. Report DNS failures, TLS errors, HTTP errors and timeouts separately rather than "zero ms".
- **Do not claim global internet speed, upload capacity or ISP outage** from a single server's HTTP latency. Local cable/Wi-Fi issue vs target failure is not certain.
- No port scanning, credential checking or probing arbitrary hosts without clear authorization; avoid uncontrolled traffic.

## 4. World map: lightweight first, real coordinates

**Avoid mandatory paid maps/tile providers for V1.** For the initial worldwide earthquake view:

- Use a **bundled, credited low-resolution world-landmass/coastline outline** from **Natural Earth** 1:110m public-domain dataset: https://www.naturalearthdata.com/ and https://www.naturalearthdata.com/about/terms-of-use/.
- Bundled map geometry may be converted once during development to a compact file (e.g. simplified GeoJSON or SVG) and loaded into an Avalonia drawing control. Record the source dataset/version and include licence/attribution note. Confirm actual asset integrity before committing; don't copy unverified art from a search screenshot.
- Use one clearly specified projection initially: **equirectangular** coordinate conversion from longitude/latitude to widget pixels (or another simple, fully documented projection). Test antimeridian, high latitudes, wide/narrow windows, DPI and scaled/panned map coordinates.
- Show coastline/background only by default; USGS quake circles over map, with size/colour keyed to actual magnitude/depth only with an always-visible legend. Handle unreported magnitudes as unknown, not 0.
- Click a pin to select its event; show accurate details in adjacent panel and a link to USGS.
- Wheel zoom anchored at the pointer, drag-to-pan and Fit World belong in IO2, alongside selectable pins and filtered lists. Clamp transforms to useful world bounds and keep hit testing consistent with the rendered projection. Clustering/LOD/density behavior remains deferred. Keep map labels minimal; avoid unsupported routing or live satellite map illusions.
- The pin layer should be redrawn **when data/filters/map transform change**, not on a forever running frame timer. Cache coastline drawing geometry and only rebuild if size/projection changes.
- The map itself must render offline from bundled geometry; cached earthquake data can display a prominent **STALE/OFFLINE** timestamp.

**Do not depend on free OpenStreetMap public tile servers for background/offline use**. OSMF policies require attribution, distinct application User-Agent, caching, and forbid bulk/offline tile prefilling (https://operations.osmfoundation.org/policies/tiles/). An online streets/zoom tile mode is a separate later feature with explicit provider policy review.

## 5. User experience / layout

Suggested standalone window title: **BEN TOOLBOX / INTERNET OBSERVATORY**, preferred window size around 1440×900, responsive smaller-window layout and scrollable side panels. Keep restrained cyan and amber, green for verified healthy states and red for actual errors.

**Top status bar:** Source health / Last updated UTC / Offline warning / REFRESH NOW / AUTO REFRESH toggle. Show readable retrieval age (e.g. Fetched 2 minutes ago) separately from provider/event timestamps. A failed refresh must retain the last successful snapshot, with an explicit STALE/OFFLINE warning and error; never replace useful cached events with an apparent successful empty result. Update age text on a modest visible-only cadence rather than a frame loop.

**Module pages:** Give each implemented module a dedicated page: earthquake map/list/details, space-weather charts, website target list, and network probes/chart. Avoid a crowded all-modules dashboard or a persistent world map behind unrelated data. The first release is a polished earthquake explorer; add NOAA only after IO0–IO2 works well.

**Left navigation inside tool:** OVERVIEW, EARTHQUAKES, SPACE WEATHER, WEBSITE WATCH, NETWORK (disabled/"Coming later" until each module truly implemented; never clickable fake screens).

**Visual direction:** Large dark map, restrained cyan coastlines, readable event markers, compact detail panel, and an unambiguous selected-event highlight ring. Avoid constant flashing/pulsing dots or ornamental animation loops. Keep source links, legends and text equivalents readable.

**Centre:** A large live-data world map on earthquake/overview view; on NOAA page a real small trend chart; no need to show a world map behind unrelated website/network metrics.

**Right context panel:** selected quake event details, source, last update, filters; or selected target data when relevant.

**Footer/status:** provider/source links, requests in flight, errors and cache age. Distinguish 1) provider data timestamp, 2) last successful retrieval, 3) UI's local current clock.

**Main cards:** 24-hour earthquake count (from selected feed/filter), strongest magnitude (if known), last source refresh, NOAA Kp later, monitored websites online count later, network latency later. Never hardcode numbers or present placeholder as live.

**Interactions:** Filter earthquakes by minimum magnitude, window (past hour/day/week, fetch right data feed), sort/recent, click marker to synchronize list/details, open official USGS page. Numeric controls in established Toolbox style: slider plus compact typed number, no giant up/down spinners.

**Explicit offline/demo mode:** If added for visual testing, mark entire view **DEMO DATA**, not merely a small label. Normal user sessions should load actual last cached records or display no data with a clear explanation.

## 6. Shared InternetData infrastructure (keep small)

Implement one reusable, app-scoped network utility to avoid each module owning duplicate timers/retry logic:

- **One long-lived HttpClient** with identifying \`User-Agent\`, appropriate timeout, allowed HTTPS sources and disposal on application exit. Do not instantiate per poll.
- **Typed, minimal provider adapters** (USGS, NOAA, optionally Website/Network). No plugin framework or generalized dependency injection architecture necessary for MVP.
- **Observable module state** or an equivalent simple state record: NotLoaded / Loading / Fresh / Stale / Error / Disabled; include \`FetchedAtUtc\`, \`ProviderTimeUtc\`, last error and data count.
- **Bounded data sizes/time:** enforce max HTTP body size, parse/record caps, cancellation tokens, timeouts, validation of lat/lon and numeric fields. Use JSON APIs; never eval remote code.
- **HTTP cache etiquette:** If-None-Match/ETag, If-Modified-Since where supported; accept 304 as still-current cached data without duplicating entries; honour Retry-After and back off on 429/5xx.
- **Scheduling:** Each visible module may own a single cancellable refresh loop driven by a shared scheduler/coordinator. Don't run every module every few seconds when hidden; stop/pause on window close and optional Refresh OFF. No 24/7 background monitor in first version.
- **State updates:** Snapshot parse/cache off UI thread, publish new immutable bounded data snapshot on Avalonia UI thread. Group updates and redraw only when meaningful state changes.
- **Caching:** Small versioned user-local JSON cache under \`%LOCALAPPDATA%\BEN_TOOLBOX_2_0\InternetObservatory\` plus settings. Persist recent earthquake/NOAA summaries if useful, not huge full event histories or grid payloads indefinitely. Atomically replace, validate on load, cap disk/records/time, show stale status when offline. Avoid secrets and exact address/location collection.
- **Notifications:** Initially in-app only on genuinely new major earthquake events or failed/recovered site checks; future Windows toast or scheduled background notifications require explicit design and permission. No fake "24/7 alerts" while app is closed.
- **Accessibility:** keyboard filter navigation, readable contrast and text equivalents for every map/chart finding; low visual clutter.

### Rate/cadence and workload baseline

| Module | Recommended first interval | While hidden/closed |
| --- | --- | --- |
| USGS daily earthquakes | Every 2 minutes + manual refresh | Pause; cache last snapshot |
| NOAA Kp | Every 5 minutes + manual refresh | Pause |
| NOAA large aurora grid (later) | Only on aurora page, >=5 min subject to provider policies | Pause |
| Website checks (later) | >=5 minutes per URL, stagger per host; cap total targets | Pause |
| Network checks (later) | Every 10–30 seconds while Network view open, configurable | Pause |

These are safe **starting suggestions**, not guarantees or contractual feed freshness. Deduplicate shared requests, avoid refreshing hidden modules and don't poll faster than official upstream refresh. Explicit Retry-After and server load constraints override client defaults.

## 7. Suggested file/folder layout

Start this new tool in the user-approved feature-based layout, without reorganising existing tools or undertaking the broader cleanup task:

- `Features/Inspectors/InternetObservatory/InternetObservatoryWindow.cs` — root window, active page and UI coordination.
- `Features/Inspectors/InternetObservatory/WorldMapView.cs` — projection, coastlines, pan/zoom and quake hit testing.
- `Features/Inspectors/InternetObservatory/ObservatoryModels.cs` — small typed records.
- `Features/Inspectors/InternetObservatory/UsgsEarthquakeService.cs` — bounded HTTP retrieval, parsing and freshness/cache state.
- `Features/Inspectors/InternetObservatory/ObservatorySettingsStore.cs` — validated user preferences and bounded cache.
- Future module-specific services/controls live beside these files when their phases are assigned; do not create placeholder implementation files now.
- `Assets/InternetObservatory/` — attributed Natural Earth map geometry.
- `Tests/InternetObservatorySmoke/` — fixture/fake-handler, projection, transform and persistence checks.
- `Notes/Plans/INTERNET_OBSERVATORY_IMPLEMENTATION_PLAN.md` — authoritative scope/status.

Avoid a giant provider framework or unnecessary subfolders. Reuse genuinely shared controls where appropriate. Read existing TelemetrySparkline and dashboard code before inventing new chart infrastructure. Do not modify/replace main Dashboard V2. See [future folder cleanup](FEATURE_FOLDER_CLEANUP_TASK.md); that broader refactor stays deferred.

## 8. Phased Codex implementation — one verified step at a time

| Phase | Outcome | Status |
| --- | --- | --- |
| **IO0 — Architecture/contracts** | Confirm current repo code, USGS schema, Natural Earth asset/projection, module routing, data freshness and bounded worker lifecycle | **NOT STARTED** |
| **IO1 — Earthquake data engine** | Typed USGS feed, parsing/validation, timeout/cache/ETag, dedupe and fixture smoke tests | **NOT STARTED** |
| **IO2 — World map / Avalonia MVP** | Native Inspect tool, polished earthquake explorer, wheel zoom/pan/Fit, presets, real pins/list/details, freshness/offline status and basic saved view | **NOT STARTED** |
| **IO3 — NOAA space weather** | SWPC Kp parser, timeline/chart, source timestamp, optional link to aurora forecast; zero fake data | **NOT STARTED** |
| **IO4 — Website monitoring** | Add/remove URL targets, status/latency snapshots, optional readable change diff and bounded history | **NOT STARTED** |
| **IO5 — Network health** | HTTP/DNS probes, rolling latency/status charts, separate errors, active-view-only probes | **NOT STARTED** |
| **IO6 — Settings, alerts, refinement** | Extend IO2 persistence to later-module watch lists/settings, in-app event/change alerts, UI refinements and optional aurora overlay if justified | **NOT STARTED** |
| **IO7 — End-to-end validation** | Windows build/visual checks, offline, cancellation, scalability, source URLs/attribution, documentation status | **NOT STARTED** |

### Recommended first implementation task: IO0 + IO1 + IO2 only

Get a **polished earthquake explorer with authentic USGS data**, rather than building five stub modules. IO2 now includes basic preferences: selected implemented page (initially Earthquakes), feed preset, filters and map position/zoom. Save these as small versioned user-local settings, validate/clamp on load, and debounce writes or save on explicit changes/close, not every pointer movement. Recover gracefully from corrupt settings. IO6 extends persistence to later modules; it does not delay these basics.

**IO0 acceptance**
- [ ] Inspect \`ToolCatalog\`, native launcher, dashboard telemetry and existing visual style.
- [ ] Validate USGS daily feed and coordinate/time units using actual sample response.
- [ ] Choose/include a **real permitted Natural Earth geometry asset** and record source/attribution.
- [ ] Define data shapes, safe data cache size, fetch policy and how active view controls polling.
- [ ] No new top-level sidebar category or fake active panels.

**IO1 acceptance**
- [ ] Inject fake \`HttpMessageHandler\` to test successful USGS feed, empty list, null magnitude, null coordinates, bad JSON, wrong content type, oversized content, timeout, 304 and 429 with Retry-After.
- [ ] Event ID dedup, update timestamps and correct \`[lon, lat, depth]\` parsing; never swap lat and lon.
- [ ] UTC provider times with correct milliseconds-to-seconds conversion and local UI format.
- [ ] Cancellation on window close and stale response suppression.
- [ ] Minimal data retrieval when Observatory not open; no background network work after close.

**IO2 acceptance**
- [ ] Open File/Inspect navigation → **Internet Observatory**; separate native window launches without touching Dashboard V2.
- [ ] World map displays land/coastlines, actual earthquake pins accurately placed, selected event opens true details/USGS URL.
- [ ] List and map filters correspond exactly; clicking list row highlights correct pin with a readable selection ring.
- [ ] Pointer-anchored wheel zoom, drag pan and Fit World work; projection/hit testing stay aligned after transforms, resize and DPI scaling. Test antimeridian/high-latitude cases.
- [ ] Past 24 hours, Past week and Significant events presets identify their exact USGS feed/period/magnitude coverage; all counts describe that selected feed/filter.
- [ ] Selected implemented page, preset, filters and map view survive reopen; invalid/corrupt settings safely fall back to defaults without excessive writes.
- [ ] Source, fetch age, warnings and offline state visible; no "LIVE" badge on stale data.
- [ ] A blank map should not silently appear as success when feed fails: retain the last successful data, display failure/stale warning and readable retrieval age separately from provider timestamps.
- [ ] Large result sets keep UI responsive, marker drawing cached/filtered; keyboard and window resize work.

**IO3 acceptance**
- [ ] NOAA parser matches **current** NOAA JSON schema; handles schema changes or failure as warning, not fabricated 0 Kp.
- [ ] Kp value and timestamps accurate, manual/interval refresh, chart with gaps for missing data.
- [ ] Space weather data fetched only when visible, not in earthquake view.
- [ ] Aurora is clearly identified as a forecast and kept separate from event pins.

**IO4 acceptance**
- [ ] Per-URL success/failure/changed/unknown are distinguishable; no leaking private URLs/tokens.
- [ ] Redirect protection, public-host validation, rate limits, per-host staggering and content size limits tested.
- [ ] JS-rendered/dynamic pages identified as limitation, not incorrectly compared as static content.
- [ ] History bounded and only checked while app view active.

**IO5 acceptance**
- [ ] Public targets opt-in/clearly identified; HTTP/DNS latency measured, missing/error not plotted as zero.
- [ ] No expensive speed-test bandwidth use or unauthorized network scans.
- [ ] Clean pause/cancellation and timestamp display.

**IO6–IO7 acceptance**
- [ ] Persist/reopen settings and bounded cache; app remains responsive with no internet.
- [ ] Alerts only for new events or relevant state transitions, with dedupe and user toggles; no push/24-7 promise when closed.
- [ ] \`dotnet restore\`, \`dotnet build\`, module tests and manual Windows Avalonia checks **actually** run and outputs recorded; no false test claims.
- [ ] Existing Dashboard V2, YouTube Audio Downloader, Game Dev and file utilities remain functional.
- [ ] Document CPU/memory/network costs measured with active module vs idle/closed; no orphan timers/tasks.

## 9. Risks and design pitfalls

- **Fake/illustrative feeds or numbers:** Absolutely no hardcoded earthquake magnitudes, Kp values, live dots, or online counts. Source-driven or clearly DEMO.
- **Maps and providers:** No high-volume OSM tile fetches or unlicensed screenshot-as-basemap use. Natural Earth is public domain and appropriate for a bundled world silhouette.
- **External provider changes:** NOAA may change JSON formats; isolate parser + fixture tests and surface service unavailable rather than hiding failures.
- **Conflating "last fetched" with "event time":** Keep timestamps separate and show the cache age.
- **Excessive background work:** No nonstop hidden HTTP polls, redraw timers, unchecked request multiplication or 1-second "real-time" loops.
- **Misleading connectivity:** One failed site doesn't mean "the Internet is down".
- **Monitoring arbitrary targets:** Never use this as an internal-network scanner. User-entered URL validation and redirect restrictions matter even in a local desktop app.
- **UI size:** Resist enormous noisy dashboards; readable list/map details matter more than ornamental scrolling code.
- **Module sprawl:** Each phase must work independently. Do not add empty fake tabs and claim all sources integrated.

## 10. Later extensions (explicitly defer)

- Optional geographic search/notifications for areas chosen by the user, without collecting precise device location implicitly.
- Rich NOAA aurora intensity overlay accurately projected from OVATION grid, with forecast issue/valid timestamps.
- Weather overlays via properly licensed API where useful, aircraft/vessel tracking **only** after data provenance and terms are checked.
- Global time zones/daylight shading and sunrise/sunset, with accurate astronomical calculations.
- Browser screenshots or website visual diff using a supported browser component/automation approach; not necessary for initial HTML checks.
- RSS headlines/space launches/satellite passes in separate optional provider modules.
- System tray / background service for alerts, only with explicit user opt-in, process lifecycle and OS notification semantics.
- A dedicated **Internet** navigation section if the Observatory and related web tools outgrow the initial Inspect placement.
- Saved dashboards/custom pinned cards; optional external visualization/tiles with provider licence and API-key support.

## 11. Progress log and Codex handoff

- **2026-10-24 — PLAN CREATED.** Inspected existing BEN TOOLBOX modular native launcher/dashboard, verified official USGS GeoJSON feeds, NOAA SWPC data access and formats, and Natural Earth public-domain map availability. Specified staged implementation with **working USGS world map as first visual milestone**, then NOAA, website checks and network probes. **No application code, map asset or build has been produced in this documentation task. All IO0–IO7 are pending.**

**Codex starter prompt:**

> Read BEN TOOLBOX root README, \`Notes/Plans/INTERNET_OBSERVATORY_IMPLEMENTATION_PLAN.md\`, native \`ToolCatalog\` / \`MainWindow\` navigation, and the existing dashboard telemetry/visual controls. Implement **IO0–IO2 only**: a standalone native Internet Observatory in Inspect with actual USGS GeoJSON earthquake data, a legitimate lightweight world map, interactive pins, pointer-anchored wheel zoom/drag pan/Fit World, named feed presets with honest coverage/count labels, compact detail panel and selection highlight, separate provider/fetch timestamps with readable age, manual refresh, bounded auto-refresh, stale-data retention, basic saved filters/map view and fake-handler tests. Keep new tool code together in Features/Inspectors/InternetObservatory/ without reorganising existing features. Keep NOAA/website/network modules planned but not superficially implemented. Respect idle cost, provider request rates, no fake live metrics, cancellation and existing dashboard compatibility. Update documentation and make one scoped GitHub commit.

### 2026-10-24 — pre-implementation scope refinement

User approved dedicated module pages, IO2 map zoom/pan/Fit, named earthquake feed presets, clear freshness with retained stale data, early basic view persistence, restrained map-led visuals and feature-based source organisation for this new tool. These requirements are incorporated above and in IO2 acceptance. IO0–IO2 remains the first implementation task; NOAA starts afterward. All phases remain NOT STARTED. This update changes documentation only; no code/assets/build or runtime validation performed.
