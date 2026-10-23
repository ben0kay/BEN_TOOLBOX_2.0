# BEN TOOLBOX 2.0

**A modular Windows developer toolbox with a futuristic Avalonia desktop interface.** Built in **C# / .NET 10** with **Avalonia UI 12**, primarily developed in VS Code.

> **Current state — October 2026:** The dashboard, navigation and many **native Avalonia utilities are already implemented as first passes**. The project is an actively tested desktop application, **not merely a PowerShell script launcher**, but it is not yet a feature-complete replacement for every legacy tool. Feature quality and Windows build/runtime testing vary by module.

## Dashboard vision — BTX-02 Command Center

The current dashboard uses a dark navy/cyan design, left navigation and four **Available modules** cards. The next visual target is a more detailed but still usable *sci-fi developer command center*.

**Reference mockup (design target, NOT an implemented screenshot):**

![BEN TOOLBOX 2.0 Dashboard V2 design concept](toolbox%20dashboard%20concept.png)

[Open Dashboard V2 reference PNG](toolbox%20dashboard%20concept.png)

The mockup is a **visual specification**, not a source of accurate system data. Its illustrated processor, GPU, RAM, clock, percentages and drive capacities are fictional examples. Do **not** hardcode them into the production UI.

**Implementation handoff:** [Dashboard V2 D1–D5 implementation plan](Docs/DashboardV2ImplementationPlan.md) — phase checklist, architecture options, scope, performance constraints and Windows acceptance tests. **D1 visuals and D2 real telemetry have been seen running in a Windows screenshot. D3 CPU/RAM sparklines and bounded history are coded but await Windows build/visual testing; D4–D5 are planned.**

### Dashboard V2 — D1/D2 running; D3 coded (Windows testing pending)

- **Retain** the existing left sidebar, four top-level sections and **Available modules** tiles **under** the new dashboard information panels.
- Keep the dark navy background and restrained cyan accents; add subtle technical **grid overlays, corner brackets, fine dividers, HUD labels and micro-detail** without sacrificing legibility.
- Introduce **limited amber/orange highlights** for memory/storage emphasis and warnings, with green reserved for genuine success/healthy state.
- Show **real local time and date**, application/session uptime and a compact system status area.
- Show simple **CPU / memory utilization** with small rolling-history **sparkline graphs**, plus installed RAM details and available space/usage for local drives.
- Present **static hardware names/specifications** only when reliably detected; cache them instead of rediscovering hardware every refresh.
- **GPU utilization, VRAM activity, temperatures and other difficult hardware telemetry may be explicitly labelled placeholders** until their Windows-specific providers are implemented. A placeholder must never masquerade as a live reading.
- **Do not add recent-tools/history panels for now.**
- Respect a compact desktop window and make cards reflow or scroll gracefully at smaller widths.

**Performance contract for V2:** One shared, lightweight telemetry source; sample CPU/memory/drive at a reasonable cadence (e.g. every 2–5 seconds), with a small bounded graph history. Time display may refresh more frequently, but only while needed. **Suspend polling and graph updates when the dashboard is not visible**, avoid per-control timers/full redraws, cache static decorative geometry, and dispose event subscriptions/timers correctly. Keep Windows-specific monitoring code separate from Avalonia layout. Do not require administrator privileges or add a heavy monitoring dependency just for a dashboard effect.

**D1–D3 implementation:** The dashboard has a 1920×1080 preferred window size (clamped to the display working area), responsive HUD/status/hardware cards, and the original four module navigation tiles. **D2** provides actual local time/date, app uptime, Windows system-wide CPU load, physical RAM, CPU model and fixed-system-drive statistics. **D3** introduces live **cyan CPU and amber RAM sparklines** based on the **same CPU/RAM readings every ~2 seconds**; a bounded **60-sample history** covers roughly two minutes, with missing readings shown as gaps, not fake zeroes. Graphs do not have their own timers. Switching away from the dashboard stops telemetry; on return, both charts begin fresh. Disk data still refreshes about every 30 seconds. GPU utilization/temperature and VRAM remain labelled as unconnected. **D3 is coded but has not yet passed Windows build and visual validation**; see the [plan](Docs/DashboardV2ImplementationPlan.md).

To validate the D3 history model independently on a machine with the .NET 10 SDK:

```powershell
dotnet run --project Tests/TelemetryHistorySmoke/TelemetryHistorySmoke.csproj
```

## What the application does today

BEN TOOLBOX has four primary navigation sections. Many tools now open **native Avalonia windows**, with scanning/processing logic implemented in reusable services rather than only in UI click handlers.

| Section | Tool | Current implementation |
| --- | --- | --- |
| **Game Development** | Artwork Design Prompter | Native fantasy-top-down / sci-fi prompt designer; category presets; original + optional sibling prompt (charred, snow, dead, etc.); original/root matching; project-reference guidance; **COPY BOTH (2 IMAGES)**. Text prompts only, not image generation. |
| | Sprite Splitter | Native PNG sprite-sheet cutting, suggested/manual cuts and export controls. |
| | Sprite Batch Tool | Native bulk PNG resize/rename, preview, drag-and-drop inputs, configurable export and optional RAW preservation. |
| | Resource Checker | Native **read-only** check for missing static Godot `res://` references; **not** an image gallery or a comprehensive Godot validator. |
| **File Toolbox** | Duplicate File Finder | Native file comparison, hash verification and guarded Windows Recycle Bin operations; destructive actions require care. |
| | Folder Backup | Native non-overwriting backup planning/preview and execution with progress/cancellation. |
| | Clipboard to PNG | Native snip/clipboard PNG saver; Windows global `Ctrl+Shift+V` hotkey while the app runs. |
| **Project Inspectors** | **Sprite Inventory** | **Standalone native inspector**: image format, dimensions, file size/path, filters/sorting and one on-demand scaled preview. Not inside Resource Checker. |
| | File Size Inspector | Native read-only ranking of the largest project files. |
| | Asset Usage Finder | Native read-only tracing of direct/indirect Godot resource references, with limits for dynamic paths. |
| | Lighting & Shadow Inspector | Native read-only exported settings/shader/scene inspection; not live Godot lighting. |
| | Biome Population Inspector | Native read-only biome/resource population and registration inspection; schema-sensitive. |
| | Items & Resource Inspector | Native read-only content browser for the **isometric-new-2d ContentIndex.json schema-v1 export** (items, drops, recipes, biome bindings); not a general fantasy-2d content editor. |
| **Settings** | Settings page | Navigation/visual shell exists; richer settings and shared project selection are future work. |

The original scripts remain under **`BEN_TOOLBOX_OLDVERSION/`**. Native conversions do **not** necessarily have full feature parity with those scripts. Test any action that writes, exports, moves or deletes files on disposable data first.

### Current limits worth remembering

- **Sprite Inventory** reads image headers efficiently; previews only the selected image. It is **not** an unused-asset detector, importer, image-corruption verifier or texture VRAM profiler.
- **Resource Checker** checks only supported static `res://` strings; dynamic loads, broken UIDs and application/runtime validity require separate checks.
- **Asset Usage Finder** helps trace usage but cannot guarantee every dynamic or runtime reference is discovered.
- **Sprite Batch** has not yet ported the original PowerShell tool's Godot Plant/Tree `.tres` generation workflow.
- **Artwork Prompter** can include GitHub sprite-reference links in generated prompts, but cannot inspect remote PNG pixels on behalf of the receiving image AI. Attach real images if the destination assistant cannot browse.
- **Items & Resource Inspector** reads a supported exported schema; there is **no general external Avalonia authoring/catalog system for the fantasy game yet**.
- **Clipboard to PNG** requires the application to be running for its hotkey; it is not a background Windows service.

## Install, run and test

**Requirements:** Windows 10/11, .NET 10 SDK. VS Code and C#/Avalonia extensions are optional but recommended for development.

From the project root:

```powershell
dotnet restore
dotnet build
dotnet run
```

Sprite Inventory's stand-alone metadata scanner also has a small console smoke test:

```powershell
dotnet run --project Tests/SpriteInventorySmoke/SpriteInventorySmoke.csproj
```

For tools that inspect Godot content, choose the correct project root containing `project.godot`. Folder Backup, Duplicate Finder and Sprite Batch act on the user-selected input/output locations; inspect previews and destination paths carefully.

**Security/maintenance note:** Current `BEN_TOOLBOX_2_0.csproj` pins `SixLabors.ImageSharp` **3.1.12**, for which NuGet has reported vulnerability warnings. **Updating and compatibility-testing this dependency is outstanding work**, not already completed. Do not suppress advisories without assessing them.

**Testing status:** Several modules were built/committed without full graphical validation in the development environment. A successful static code review or GitHub commit is **not** proof of a successful Windows build. Run the commands above and visually test the affected windows after changes.

## Repository organization

| Path | Responsibility |
| --- | --- |
| `App.axaml` / `App.axaml.cs` | Avalonia application and theme initialization |
| `Views/MainWindow.axaml` / `.cs` | Current dashboard, navigation and native tool launch routing |
| `Views/*Window.cs` | Dedicated native tool/inspector windows |
| `Services/ToolLauncher.cs` | Allowlisted tool catalog and legacy PowerShell launcher |
| `Services/InspectorServices.cs` | Resource integrity and file-size scans |
| `Services/SpriteInventoryService.cs` | Background, metadata-only sprite inventory |
| `Services/ArtworkSiblingPromptService.cs` | Data-driven artwork prompt and sibling-variant recipes |
| `Services/*Service.cs` | Other processing/scanning implementations |
| `Models/`, `ViewModels/` | Shared data contracts and presentation scaffolding |
| `Assets/` | Application assets |
| `Docs/` | Feature guides and current technical notes |
| `Tests/SpriteInventorySmoke/` | Independent sprite-header smoke tests (excluded from main app compile) |
| `BEN_TOOLBOX_OLDVERSION/` | Preserved legacy PowerShell tools |
| `toolbox dashboard concept.png` | **Visual design reference only**, not part of the current working dashboard |

Important tool details:

- [Artwork Design Prompter](Docs/ArtworkPrompter.md)
- [Sprite Inventory](Docs/SpriteInventory.md)
- [Items & Resource Inspector](Docs/ContentInspector.md)

**Planned Game Development module:** [Pixel Art Converter implementation plan](Notes/Plans/PIXEL_ART_CONVERTER_IMPLEMENTATION_PLAN.md) — a multi-PNG drop queue, first-file live selected preview with a pixel-width slider, independent PNG export/rename, and a true converted-image handoff to Sprite Batch before final output. **Planning only; no implementation yet.**

## Architecture and engineering principles

1. **Native-first where practical.** Avalonia owns the desktop presentation. Put reusable scanning, filesystem work, processing and cancellation in dedicated services; preserve compatibility with legacy workflows when it matters.
2. **Data-driven authoring.** Prefer shared, configurable models/presets over per-tree, per-biome or per-project hardcoding. Integration with Godot `.tres` schemas and content catalogs remains future work.
3. **Safe filesystem access.** Inspectors are read-only. Writing tools should validate paths and preview operations, avoid overwrites by default, guard destructive actions and report partial/cancelled work honestly.
4. **Performance-first UI.** Avoid scanning entire trees or decoding every image at startup. Scan on demand, filter cached results, use virtualized collections, cap preview memory, keep redraw work bounded, and suspend background activity when not visible.
5. **Scoped commits and honest validation.** Keep changes focused, update relevant docs, prefer one commit per coherent task, and distinguish code inspection from actual `dotnet build` and Windows UI/runtime verification.
6. **No arbitrary remote shell.** The present PowerShell launcher is explicitly allowlisted and **local-only**. Do not expose it directly to a network or mobile device.

## Roadmap and vision

| Area | Status | Next direction |
| --- | --- | --- |
| Avalonia shell / four workspaces | **Implemented** | Refine visuals without breaking navigation |
| Core native tools | **First passes implemented** | Resolve local build/runtime issues and compare with legacy feature parity |
| Native read-only inspectors | **First passes implemented** | Better validation/reporting where useful, without duplicating scanner responsibilities |
| **Dashboard V2 — BTX-02** | **D1/D2 seen running; D3 coded — Windows verification pending** | D4 visual polish; D5 verification; advanced GPU readings deferred |
| Shared project/workspace selection | **Planned** | Remember user-selected local Godot projects and offer useful deep links; do not assume a project folder |
| Shared job engine | **Planned** | Typed jobs, progress, bounded concurrency, cancellation, logs and safe lifecycle |
| Native feature parity / content authoring | **Incremental / future** | Port selected remaining PowerShell features; explore reusable Godot asset and Resource authoring with schema validation |
| Authenticated Windows local-network API | **Future** | Allowlisted job requests only, no raw shell execution |
| Android / mobile companion | **Future** | Control *approved jobs on the Windows PC*, not run PowerShell on Android |
| Private access outside home | **Optional future** | Strong auth, encryption, restricted permissions, auditability |

### Long-term direction: desktop tool + optional phone companion

Eventually separate **desktop/mobile presentation**, a shared **typed tool/job engine**, **Windows-local execution adapters**, and an explicitly opt-in, authenticated API. The phone should request approved jobs on the Windows machine and observe progress/results. Network features are **not present today**, and Avalonia does not provide secure remote execution automatically. Require encryption, authorization, validated filesystem scopes and extra safeguards around deletion before enabling such features.

---

**README status:** Native tools, D1/D2 dashboard and D3 graph code recorded as of 2026-10-23. D2 measurements were visible in a user screenshot; D3 needs a Windows build/screenshot and its smoke test. D4/D5 remain future work. GPU sensor percentages/temperatures are not connected.

### Pixel Art Converter preview

Game Development → Pixel Art Converter provides a multi-PNG queue and shared live detail slider (including 32/64/96/128/256 presets). Logical pixels are enlarged sharply back to each source’s original canvas dimensions; alpha is preserved. Only the selected image is processed. Preview implementation awaits Windows build/visual validation. Export and Sprite Batch handoff remain planned. See [the implementation plan](Notes/Plans/PIXEL_ART_CONVERTER_IMPLEMENTATION_PLAN.md).

Pixel preview refinements: automatic 16/32/64-colour palettes, Endesga 32/grayscale, RGB or perceptual matching, brightness/contrast/saturation, optional Bayer dithering, and logical-grid Fit/integer zoom. Settings are shared across queued files; automatic palettes derive from each individual image. Windows build/visual validation remains pending.

Custom palette editor: Edit / Create Palette, select a swatch, adjust RGB or apply a hex colour, and Save as New Preset. Edit Endesga through a private copy; built-ins remain unchanged. Saved palettes persist locally and appear in the palette dropdown. These presets save colours only. Windows build/runtime validation pending.

Pixel converter P3/P4 are coded (Windows validation pending): expand **Export / Send to Sprite Batch**, choose output folder and naming, optionally select **Export selected only**, then **Preview Output → Export PNGs**. Alternatively **Send All to Sprite Batch** transfers converted copies without requiring a prior export; choose final naming/size/destination in Batch. Handoff defaults to nearest resizing and disables RAW archiving of the staged copies. Batch supports scaling up to 8× within image-size limits. Converter output retains original canvas dimensions. Run `dotnet run --project Tests/PixelArtSmoke/PixelArtSmoke.csproj` for service checks.

Pixel converter also has **Manual files** (default) and **Folder processing** modes with separate queues. Folder mode scans PNGs with optional subfolders, displays relative paths, and can export copies preserving subfolders or explicitly overwrite sources after **Preview Output → Overwrite N PNGs**. Overwrite retains original backups in each source directory’s `.pixel-backups/{job}/` as `.bak` files, prepares conversions before replacement, and attempts rollback on cancellation/failure. Backup folders are excluded from scans. Overwrite build/Windows validation is pending; backup restore is currently manual.

Pixel converter edge controls: Average↔Nearest sampling blend, hue, alpha cutoff, disconnected-island removal and None/Outside/Inside outlines with width/hex colour. Edge controls work on the logical grid; default cutoff/cleanup/outlines are off. They apply to preview, export, folder overwrite and Batch staging through the same pipeline. Palette-only presets still save colours only. Windows validation pending.

Pixel converter layout: input/file list left, zoom/pan preview centre, scrollable grouped controls/export right. Preview now updates during slider dragging using a single throttled conversion loop and renders the final settings after dragging stops; actual speed depends on conversion cost. Windows build/layout validation pending.
