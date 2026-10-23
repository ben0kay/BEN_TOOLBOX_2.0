# Dashboard V2 Implementation Plan — BTX-02 Command Center

> **Status: D1 VISUALLY ACCEPTED BY USER; D2 CODED — WINDOWS BUILD/RUNTIME VERIFICATION PENDING; D3–D5 NOT STARTED**  
> **Last audited:** 2026-10-23  
> **Repository:** [ben0kay/BEN_TOOLBOX_2.0](https://github.com/ben0kay/BEN_TOOLBOX_2.0) (main)  
> **Purpose:** Implementation handoff for a future developer / ChatGPT / Codex session. This document describes work to perform; it does **not** claim that the design mockup or telemetry are already implemented.

## 0. Read this first (future session handoff)

Before writing code:

1. Read the root [README](../README.md), this plan, the project file `BEN_TOOLBOX_2_0.csproj` and current `Views/MainWindow.axaml` / `Views/MainWindow.axaml.cs`.
2. Open the **actual visual target**: [toolbox dashboard concept.png](../toolbox%20dashboard%20concept.png). The file is in the **repository root**, *not* `Dashboard-V2-Concept.png`. Do not recreate the mockup by using it as a fixed full-screen image; implement real Avalonia controls and styling.
3. Read `Services/ToolLauncher.cs` to understand the tool catalog and inspect the working main navigation callbacks before rearranging UI.
4. Confirm the current build state with `dotnet build` if possible. When not possible, explicitly note that Windows compile/runtime verification remains outstanding.
5. **Only implement the phase requested**; keep commits small, preserve the existing tool windows and update the status table below and relevant README after completing each phase.
6. Do not treat proposed files or services in this plan as already existing; check the repo first. Respect any newer user preferences or repository instructions.

**Current baseline verified 2026-10-23:** Avalonia 12.1.4 / C# / .NET 10; main dashboard in `Views/MainWindow.axaml`, navigation and tool launching in `Views/MainWindow.axaml.cs`. Main window now requests 1920 × 1080 with 900 × 600 minimum (working-area clamp on smaller displays) and a 242 px left sidebar. Dashboard includes a hero/status panel and four navigation tiles (Game Development, File Toolbox, Inspectors, Settings). The same MainWindow contains alternate tool-list sections; `ShowSection()` toggles dashboard/tiles and other workspaces. Several native tool windows and services already work. **D1 static visuals were accepted by the user. D2 local clock/physical RAM/CPU/disk providers and dynamic wide-window layout are now coded, but Windows build/runtime measurement verification is still outstanding. No D3 history graph renderer exists.**

## 1. User-approved visual direction

Aim for the visual character in the concept PNG: a premium dark navy, futuristic technical workstation with readable white text, cyan HUD line work, sparing teal/green success and amber/orange caution/accent details.

**Keep unchanged in principle:**
- Existing sidebar/logo and the Dashboard / Game Development / File Toolbox / Inspectors / Settings navigation scheme.
- The **four Available modules cards below the dashboard status/telemetry area**, with their existing click actions and descriptions.
- Working native tool launch routes, file services, inspector UI and existing Godot tooling.
- The entire window remains a functional Avalonia application, not a screenshot with hotspots.

**Add / change:**
- Thin technical overlay grid, corner brackets, tick marks, section labels, targeted separators, restrained glows and cyan/amber highlights; do not produce dense, unreadable cyberpunk noise.
- Digital local clock and date in the upper command area; optional clearly labelled application uptime.
- Compact system-status / session information with truthful status wording (remove obsolete "PowerShell integration is scheduled for Stage 2" text).
- CPU/RAM monitoring cards with small rolling sparkline graphs and labels for what is actually being measured.
- System/hardware summary and **local drive capacity/free-space progress** using actual available readings when D2 begins.
- A future GPU card may show a discovered hardware name but must label GPU utilization, temperatures and detailed VRAM telemetry **"Not connected", "Unavailable" or "Planned"** until implemented; no fabricated values.
- Responsive composition: at narrower desktop widths, use appropriate wrap/stack/scroll behavior instead of tiny clipped cards.

**Explicitly out of scope for now:** recently opened tools or activity history, notifications, remote client/API, GPU driver integration, temperature probes, third-party hardware sensor packages, fancy continuous particle effects, project selection, or refactoring unrelated inspectors.

**Reference mockup warning:** any date, percentages, AMD/NVIDIA hardware names, capacity values, graph histories and system states *drawn in the concept art* are illustrative **not detected facts**. Never hardcode or imply that an illustrated value is a live measurement.

## 2. Planned implementation phases

| Phase | Work | Status | Validation gate |
| --- | --- | --- | --- |
| **D1** | Dashboard layout foundation / static HUD styling | **VISUALLY ACCEPTED BY USER — FORMAL BUILD/REGRESSION STILL PENDING** | UI builds; four module buttons and all navigation work; layout resembles target at multiple sizes |
| **D2** | Real local clock, CPU/RAM & disk data, hardware descriptions | **CODED — WINDOWS BUILD/RUNTIME TEST PENDING** | Readings are real and labelled; safe unavailable fallbacks; no fake values |
| **D3** | Tiny telemetry graphs and bounded sampling lifecycle | **NOT STARTED** | Graphs reflect samples; no redundant polls; no updates when dashboard hidden |
| **D4** | Final sci-fi styling and visual polish | **NOT STARTED** | Smooth, legible, restrained glow; no excessive idle render/animation |
| **D5** | Windows tests, performance audit, regression pass, docs reconciliation | **NOT STARTED** | Checks below completed and known issues recorded |

Phases should be implemented **in order**. Commit each independently where practical. Never mark a phase complete because code was merely pushed; distinguish "coded", "build passes", "visually checked" and "accepted".

### D1 — Dashboard foundation (first implementation task)

**Goal:** Make the current dashboard *visually resemble the mockup* while leaving its non-dashboard behavior and telemetry architecture safe to expand.

Tasks:

- [ ] Review the current content grid, section switching and `x:Name` references in `Views/MainWindow.axaml` and the `ShowSection()` logic.
- [ ] Add a dedicated dashboard content region / presentational components. Either integrate carefully in existing AXAML for the first pass **or** extract to `Views/Dashboard/DashboardView.axaml` plus code-behind if the isolation is low-risk; avoid a speculative architecture-wide refactor.
- [ ] Separate dashboard-only content from the shared tool-list section so changing to Game / Files / Inspect does not leave dashboard telemetry or decorative overlays on screen.
- [ ] Preserve **the four existing module tiles and their actual click handlers**. They remain **under** the new status/hardware region.
- [ ] Add clock area, status panel, compact CPU/GPU/RAM/drive shell panels and technical heading/dividers; values can be clearly labelled placeholders (e.g., `—` / `Awaiting telemetry`), not fake percentages or hardware models.
- [ ] Use shared reusable styling for HUD panel borders/corners, technical micro-labels, separators and accent colors instead of duplicating large amounts of XAML.
- [ ] Correct obsolete Stage 2/PowerShell placeholder copy visible on the current dashboard. Distinguish actual desktop UI readiness from unimplemented remote APIs.
- [ ] Keep vertical scrolling and design for **1260 × 790** default plus **900 × 600** minimum; accommodate intermediate widths and Windows display scaling.
- [ ] Avoid large full-window image backdrops; draw subtle technical grid/lines via low-cost reusable graphics or brushes.

**D1 acceptance:** `dotnet build` succeeds; `dotnet run` shows new dashboard; four module tiles still navigate; native toolbox launch routes unchanged; sidebar works on return from other workspaces; no clipped essentials at min size; empty hardware values explicitly say unavailable/not connected. Screenshot compared to PNG; note any divergences. No new monitoring timer or speculative simulated telemetry required.

**Suggested scope:** `Views/MainWindow.axaml`, smallest necessary changes to `Views/MainWindow.axaml.cs`, and optional dedicated `Views/Dashboard/*` style/view components. Avoid changes to unrelated tools.

### D2 — Live local hardware & system information

**Goal:** Replace safe D1 placeholders with real, well-labelled Windows measurements.

Tasks:

- [ ] Introduce a reusable **`Services/SystemHardwareService.cs`** (or equivalent) for relatively static info: CPU identification when available, logical processor count, installed RAM, local fixed drives and their total/free capacity; cache stable values.
- [ ] Introduce **`Services/SystemTelemetryService.cs`** (or equivalent) with an interface/DTO for sampled CPU load, OS physical-memory utilization, and drive capacity updates. Keep OS-specific calls isolated from AXAML/code-behind.
- [ ] Clock: display `DateTime.Now` and local date correctly; one light dashboard-owned timing mechanism, not separate timers per label.
- [ ] CPU usage: use a *real system-wide load measurement*. A viable Windows provider is delta sampling of `GetSystemTimes`, handling the first-sample baseline and divisions/rollover. Do not confuse process CPU usage with system CPU percentage.
- [ ] RAM: use actual **system** installed/total/available physical memory, not `GC.GetTotalMemory()` or managed-process heap size. Windows `GlobalMemoryStatusEx` is a possible provider; encapsulate P/Invoke cleanly.
- [ ] Drive: `DriveInfo.GetDrives()` with ready fixed drives only; catch unauthorized/unready/removed drive exceptions; show total, free and percent used, distinguish system C: from other drives without assuming every machine uses C:.
- [ ] CPU/GPU model name: display only when a reliable, permission-safe provider exists; absence should show "Unavailable", not block the dashboard. Reading names once is enough; no expensive synchronous WMI query on the UI thread.
- [ ] GPU **utilization**, GPU **temperature**, VRAM **usage** remain placeholders unless their real providers are separately approved and validated.
- [ ] Ensure error handling and missing data never crash the dashboard; display last known reading only if its age is made clear, otherwise use "Unavailable".
- [ ] Avoid new heavy libraries/admin requirements for basic monitoring.

**D2 acceptance:** display shows plausible real date/time, physical RAM and disk readings verified against Windows system tools within ordinary sampling differences; CPU reflects load; unsupported sensors display truthful fallback; navigation stays responsive; no repeated slow hardware discovery.

### D3 — Lightweight rolling telemetry sparklines

**Goal:** Add the small live CPU and RAM graphs shown in the PNG without making the dashboard a monitoring application.

Tasks:

- [ ] Add one reusable, lightweight drawing control such as **`Views/Dashboard/Controls/SparklineControl.cs`** or a similarly scoped component; no heavy chart package for tiny graphs.
- [ ] Keep a bounded rolling history (suggested **maximum 60 samples**) for each measured series, normalize valid percentages to 0–100, handle missing readings and gaps.
- [ ] Share **one sampling pipeline** across CPU/RAM labels and charts. Do not make a separate polling timer per card or duplicate history storage.
- [ ] Target an update cadence around **2 seconds for CPU/RAM**. Update drive space at a slower cadence (e.g. 30 seconds) and static hardware info only on startup/explicit refresh.
- [ ] Stop **sampling, timers and graph invalidation** when dashboard is not displayed; restart cleanly when returning. Close/dispose resources on window shutdown. Background reads must not block the Avalonia UI thread.
- [ ] Ensure all UI-bound updates are marshalled onto the UI thread where required. Cancel/ignore work that finishes after navigation away.
- [ ] If a graph has no valid samples, show a neutral "No data" state, not an invented flat activity trace.

**D3 acceptance:** watch genuine CPU/RAM sample changes, navigate away and confirm polling stops, return and confirm it resumes, verify bounded history and no gradual memory/handler leak. Run at least a basic idle CPU observation.

### D4 — Visual fidelity and technical overlays

**Goal:** Match the PNG's finish once real layout and data exist.

Tasks:

- [ ] Refine accent palette: cyan primary, teal/green success, small amber warning/memory/storage emphasis and low-contrast neutral borders.
- [ ] Add restrained HUD corner details, fine grid, horizontal rule ticks, subtle glows, small navigation/telemetry labels and minor hover/active effects.
- [ ] Use reusable styles/controls/geometry and avoid spawning many visual nodes/overlays. Prefer cached/static decoration to frame-by-frame visual effects.
- [ ] Tune typography and spacing with actual system data lengths (long CPU names, different drive counts/capacity values), not only illustrated mockup labels.
- [ ] Confirm contrast/accessibility: status legible without color alone; no tiny critical text; color warnings indicate meaningful states.
- [ ] Confirm scrolling and reflow at minimum and default window sizes, different DPI scaling, and long project/tool names.

**D4 acceptance:** visually compared to actual concept PNG; controls remain usable; no excessive CPU/GPU use while app is idle; no redundant decorative animations; warnings are truthful.

### D5 — Regression, performance and documentation

**Goal:** Complete a reproducible verified release candidate, not merely a pretty commit.

Tasks:

- [ ] `dotnet restore`, `dotnet build`, `dotnet run` on Windows .NET 10; record exact errors/warnings and how they were handled.
- [ ] Smoke-test all five navigation entries, four existing dashboard module tiles, and representative native windows: Artwork Prompter, Sprite Inventory, Resource Checker, Sprite Splitter, Sprite Batch, File Size Inspector, Duplicate File Finder and Clipboard to PNG.
- [ ] Test window layouts at default **1260 × 790**, minimum **900 × 600**, a maximized window, and Windows scaling 125%/150% when possible.
- [ ] Check timer lifecycle: on Dashboard, after switching away, switching back repeatedly, and closing application. No duplicate polling subscriptions.
- [ ] Check drive access failures, missing sensor providers and non-Windows behavior (graceful fallback if applicable; Windows is the primary target).
- [ ] Measure a representative **idle baseline vs. new dashboard** and sample latency/memory growth; investigate regressions rather than assuming artwork effects are free.
- [ ] Add focused service tests for CPU percentage computation, capacity formatting, invalid/missing data, graph-history bounds and stop/start transitions where practical.
- [ ] Update this status table, mark verified / partially verified accurately, and reconcile the root README against the real code and actual test results.
- [ ] Do not silently change the existing `SixLabors.ImageSharp` dependency while doing dashboard work; its existing vulnerability warnings deserve a separate security/compatibility task.

## 3. Design / implementation boundaries

### Existing layout and navigation

Main window structure at planning time:

- `Views/MainWindow.axaml`: sidebar, top strip, scrollable main content, named shared page labels, existing `HeroPanel`, four module buttons, and shared `ToolListPanel`.
- `Views/MainWindow.axaml.cs`: `NavigateDashboard`, `NavigateGame`, `NavigateFiles`, `NavigateInspect`, `NavigateSettings` and `ShowSection()`; individual native tool routing in `LaunchTool()`.
- `Services/ToolLauncher.cs`: allowlisted catalog and legacy local Windows PowerShell launching; **not** a remote-control API.

**D1 current implementation choice:** Dashboard contents remain in `Views/MainWindow.axaml` under one named `DashboardPanel`, with small view-switching changes to `MainWindow.axaml.cs`. This avoids a premature new view/code-behind before D2 data binding exists. D1 uses a wrapping top status + telemetry row and wrapping four-card hardware row, plus static clock, graph grid, and honest "not connected" labels. Existing four navigation tile names and Click handlers remain unchanged. No `DashboardView.axaml` was created yet.

D1 may split views, but must preserve the same routes and capabilities. **Do not** rebuild or rewrite the inspector or game-development modules while implementing cosmetic dashboard changes.

### Proposed new files (not created by this plan)

- `Views/Dashboard/DashboardView.axaml` and `DashboardView.axaml.cs` — optional extraction for dashboard-only layout/lifetime.
- `Views/Dashboard/Controls/HudPanel.cs` / reusable XAML styles — static technical panel treatments.
- `Views/Dashboard/Controls/SparklineControl.cs` — small custom history graph.
- `Services/SystemHardwareService.cs` — cached machine metadata.
- `Services/SystemTelemetryService.cs` — platform-gated resource sampling, DTO and cancellation.
- `ViewModels/DashboardViewModel.cs` — optional observable/dashboard state; prefer clear separation over code-behind bloat.

These names are **suggestions**. Work with the repo as it exists when implementation begins; keep diffs minimal and conform to Avalonia 12 APIs rather than copying code for obsolete Avalonia releases.

## 4. Known risks and decisions

| Concern | Decision |
| --- | --- |
| Hardcoding the PNG | Prohibited. It is a **design reference only**. |
| Mockup data | Placeholder values in the image are not real facts. Label every incomplete sensor clearly. |
| GPU utilization / temperatures | Deferred; no fake live percentages or temperatures. |
| Realtime graphs | CPU and physical RAM first; lightweight lines and bounded history. |
| Polling overhead | One service, at most every ~2 sec for dynamic values; pause when hidden. |
| Available modules | Preserve existing four tiles and place **below** new dashboards. |
| Recently used tools | Explicitly **not wanted yet**. |
| Other screens / native windows | No functional behavior change in D1; smoke-test existing routes. |
| Narrow window | Scroll/reflow instead of cropping or shrinking text indefinitely. |
| ImageSharp warnings | Tracked separately; do not treat a vulnerable dependency as resolved. |
| Old Stage 2 copy | Remove during D1. Local PowerShell and native tools are already present. |
| Performance testing | Windows build and measurements required; GitHub commit ≠ validated app. |

## 5. Progress log / handoff record

**2026-10-23 — Planning document created.** Baseline and visual reference confirmed from GitHub. At initial creation, D1–D5 were all not started; no Avalonia code was modified by the planning task.

**2026-10-23 — D1 code authored (Windows verification pending).** Changes: `Views/MainWindow.axaml` now contains a dedicated `DashboardPanel` with a technical command strip, static clock placeholder, honest active-local-session panel, CPU/RAM placeholder graph frames, four hardware status cards, and the four existing module navigation tiles. The new `Border.hud` / `TextBlock.hud-label` styling reuses the current dark palette. `Views/MainWindow.axaml.cs` now toggles one dashboard parent instead of individually toggling tiles and no longer exposes Stage 2 legacy copy. No hardware polling, real clock, sparkline rendering, or GPU sensors were implemented. **Static checks:** XML tag structure and code references reviewed; **`dotnet build`, `dotnet run`, responsive visual rendering and full tool navigation are NOT verified in this environment.** Do not mark D1 accepted until someone runs the Windows tests. D2–D5 remain not started.

**2026-10-23 — D1 visual acceptance and D2 implementation.** User reported D1 "looks fine" and requested D2 alongside a **1920×1080 default window** for use on a full-HD screen. Added `Services/SystemTelemetryService.cs` (Windows `GetSystemTimes` delta-based system-wide CPU load, `GlobalMemoryStatusEx` physical RAM, `DriveInfo` accessible fixed-system-drive readings) and `Services/SystemHardwareService.cs` (cached registry processor model and GiB formatting). Added `Views/MainWindow.Dashboard.cs` partial class to manage one visible-only 1-second clock and a **non-overlapping** background sampler: CPU/RAM every 2 seconds, drives every 30 seconds. No graph history is collected yet; GPU sensors remain placeholders. `Views/MainWindow.axaml` now requests 1920×1080 and contains named live value targets, a real drive progress bar and dynamically sized status/hardware cards. Runtime clamps the requested dimensions to the primary display's available working area (physical pixels converted using DPI scaling). `Views/MainWindow.axaml.cs` calls `InitializeDashboard` and activates/deactivates polling with section navigation. **Not compiled or runtime-tested in this environment because .NET SDK and Windows UI are unavailable.** GPU model/usage, temperatures and D3 sparklines remain deliberately deferred. D3–D5 **not started**. At 1920px the layout should use the available horizontal width; at smaller window widths the cards should reflow.

Future implementers: append date, commit SHA, files changed, what was tested (and where), user feedback, known errors and the next recommended task. Keep the phase table above in sync.

**Next action:** Pull D2, run `dotnet build` and `dotnet run` on Windows. Check actual CPU/RAM readings versus Task Manager and disk versus Explorer, refresh/idle sampling, correct local clock, navigation stop/resume, 1920×1080 initial sizing and 900×600 minimum layout. Fix any Avalonia/Windows build or UI issues, then continue to **D3 sparklines** after acceptance.
