# BTX Storage Analyzer — Implementation Plan (SA0–SA4)

> Repository: BEN_TOOLBOX_2.0 · Category: **Files / File Toolbox**
> Updated: 2026-11-02
> Status: **SA0/SA1 CODED; SA2 FILE-LEVEL DENSITY + STRICT >1 KiB MAP FLOOR CODED 2026-11-03. BUILD/WINDOWS TESTS PENDING. SA3–SA4 NOT STARTED.**
> Design direction: dark navy, aqua/cyan sci-fi panels, strong treemap, largest-item table, selected-item details, file-type breakdown, largest categories. Avoid decorative UI overwhelming actual files.

## Read first
- Read the root README, `BEN_TOOLBOX_2_0.csproj`, `Services/ToolLauncher.cs`, `Views/MainWindow.axaml.cs`, and this plan.
- Existing navigation: File Toolbox tool cards are populated from `ToolCatalog.ByCategory("Files")`, and `MainWindow.LaunchTool` launches native windows.
- This tool is a separate native window, `Views/StorageAnalyzerWindow.cs`; do not refactor dashboard, downloader, backups, or inspectors as part of SA1.
- The concept illustration uses *invented* sizes and categories. Do not treat them as real machine results.
- `dotnet build` and an actual Windows launch/resize review must be performed. The GitHub file edits for SA0 were not locally compiled here.

## Immutable user decisions
1. **Never scan automatically.** Opening Storage Analyzer displays a blank dashboard and a drive picker. Do not enumerate folders, files, or read MFT until the user explicitly presses Scan.
2. **Initially scan one chosen drive**, but represent requested scan roots as a collection internally so future releases support multiple selected drives. Multiple roots must have per-root breakdown and sensible cross-root aggregation; no duplicate counts.
3. No made-up percentage-complete when total work is unknown. Display discovered bytes/files/folders and elapsed time; report partial/error results transparently.
4. Read-only by default. Do not add cleanup/delete operations to the early phases. Real Explorer actions must only reference verified genuine paths, not preview data.
5. Exclusions are applied **before traversing** matching directories; user-selectable paths, directory names, patterns, and presets. Never follow NTFS junction/reparse cycles by default.
6. The scanner must stay independent from Avalonia. Keep UI updates throttled and cancellable.
7. Distinguish filesystem size totals, allocated-on-disk size, drive reported used/free capacity, inaccessible files, and excluded content. Scanned size need not match actual used disk capacity.
8. Clearly separate synthetic design preview from actual scan results; do not automatically populate preview values.

## Phase status

| Phase | Work | Status |
| --- | --- | --- |
| SA0 | Native Files tool, blank dashboard, manual drive choice, five result regions, opt-in fictional design preview, disabled scanner action | **Coded, committed; Windows build/visual test pending** |
| SA1 | Real, cancellable, read-only metadata scanner for one selected drive, pre-scan exclusions, visible progress/errors | **Coded and committed; build/test/Windows verification pending** |
| SA2 | Efficient real treemap geometry and hit-testing, zoom/drill-down, tooltips and synchronized selection | **Code implemented and pushed; build, headless tests and Windows visual review pending** |
| SA3 | Largest-items browser, selected details, actual file-type/category summaries, Explorer reveal/open/copy path and guarded targeted rescan | Not started |
| SA4 | Measure and optimize performance, cached snapshots, optional faster NTFS/MFT engine, multi-root UI/scanning | Not started |

## SA0 — current first pass

Implementation:
- Add tool definition `storage-analyzer` to `Services/ToolLauncher.cs` in category `Files`.
- Wire `LaunchTool` in `Views/MainWindow.axaml.cs` to open `StorageAnalyzerWindow`.
- `Views/StorageAnalyzerWindow.cs`: drive picker, blank treemap, blank largest items, blank selected item, blank file type breakdown, blank largest categories. Pre-scan settings/exclusion information and clearly disabled scanner button. Explicit "SHOW DESIGN PREVIEW" toggles fictional coloured tiles, ranked items, proportional bars and selectable fake items.
- DriveInfo volume enumeration and capacity lookup are permitted *metadata-only* operations, **not directory scans**.
- Do not add timers, startup threads, sample-data auto-population, or file operations.

**SA0 acceptance checklist**
- [ ] Windows: `dotnet build` on .NET 10 and Avalonia 12 passes.
- [ ] Open Toolbox → File Toolbox → BTX Storage Analyzer.
- [ ] Opening the window does **not** scan any drive. Treemap and charts are empty, status says NO SCAN STARTED.
- [ ] Drive selection only displays volume name/capacity/free-space metadata; no full filesystem scan.
- [ ] Optional preview is visibly marked DEMO; selecting sample tiles changes sample details; clearing preview restores empty panels.
- [ ] Scan button disabled; no fake progress or fabricated system readings.
- [ ] Window readable at default size, resized, and Windows 125%/150% scaling; no major horizontal clipping.
- [ ] Other File Toolbox tool launches still work.
- [ ] User compares screen with concept and approves/requests revisions.

## SA1 — background scan engine

Proposed separations (not yet created): `Features/StorageAnalyzer/Models/StorageNode.cs`, `ScanOptions.cs`, `ScanSnapshot.cs`; `Features/StorageAnalyzer/Services/StorageScanService.cs` and `ScanExclusionMatcher.cs`. Retain current window initially; extract UI components only where beneficial.

Tasks:
- [ ] One selected drive required before Scan is enabled. Explicit Scan is the only entry to reading folder contents.
- [ ] `ScanOptions.Roots` as collection (one root in UI initially), with exclusion sets, skip-junctions default and cancellation.
- [ ] Use streaming `Directory.EnumerateFileSystemEntries` / metadata with robust exception handling at each directory; never open file content, hash, or generate thumbnails.
- [ ] Use iterative traversal to avoid recursion depth stack overflows, avoid reparse-point cycles, and handle failures without losing the rest of the scan.
- [ ] Store compact file nodes and aggregate directory sizes bottom-up; limit UI dispatches to periodic snapshots. Do not instantiate an Avalonia control for each file.
- [ ] Real activity counters (discovered files/folders/bytes, elapsed time, inaccessible items), cancel and restart; define pause/resume only if implementation is reliable.
- [ ] Apply path/pattern exclusions before entering excluded directory. Make defaults conservative, editable and visible before Scan.
- [ ] Replacing an active/completed result must never mix two runs; cancellation, generation IDs and UI-lifetime protection.
- [ ] Tests: empty/unready/removable drives, unauthorized paths, symlinks/reparse points, locked files, very deep trees, files deleted mid-scan, cancellation, exclusions and large counts.

## SA2 — treemap
- [ ] Implement squarified treemap layout of actual hierarchical size data, with stable colour mapping and a minimum visible tile size.
- [ ] Custom Avalonia drawing control (not one visual child per file); cache layout until size/data/zoom changes; hit-testing for hover and click.
- [ ] Drill down and breadcrumb; synchronized selection with file list and detail pane. Avoid labels in tiny tiles.
- [ ] Test 100K+ entries and resizing; keep pointer responsiveness.

## SA3 — analysis, detail actions
- [ ] Real file/folder ranking, sorting/filter/search and selectable records (virtualize large lists).
- [ ] Selected item: name, validated path, byte size, child counts, timestamp where available, scan timestamp.
- [ ] Windows Explorer open/reveal, copy path, and optional targeted rescan; handle deleted/moved paths gracefully.
- [ ] File-type breakdown by extension and semantic groups; document classification rules, count each file once.
- [ ] Largest categories use the same underlying sizes as the treemap, with consistent denominators.
- [ ] Disabled or hidden actions for synthetic demo data.

## SA4 — verified speed / multi-drive
- [ ] Benchmark basic scanner first on representative HDD, SSD, NVMe, external and large development folder workloads; report test methodology, exclusions and accessible file count.
- [ ] Consider NTFS MFT accelerated provider behind an interface only if normal enumeration is measurably insufficient; permissions/admin fallback and non-NTFS support mandatory.
- [ ] Optional snapshot cache with scan dates and explicit stale warning, plus partial rescan invalidation.
- [ ] Evolve selection UI to checkboxes for multiple drives; total vs per-drive results, overlapping roots and removable-drive disconnection handling.
- [ ] Audit idle CPU, memory, allocation and layout/render latency.

## Important handoff boundaries
- Do not mark phases accepted solely because source was committed.
- No deletions/cleanup feature without a separate approved safety design.
- Preserve preferred YouTube Downloader-derived BTX aqua/navy UI, but do not globally restyle unrelated windows.
- If SA0 design preview needs more fidelity, polish its look **without** quietly implementing an actual scan.

## SA1 implementation notes — 2026-11-02

**Files:**
- `Features/StorageAnalyzer/Models/StorageScanModels.cs`: immutable options, compact retained storage-node hierarchy, progress snapshots and final result. Child nodes retain parent/name rather than a full path string per file. Directory sizes and subtree counts are aggregated bottom-up in linear time.
- `Features/StorageAnalyzer/Services/ScanExclusionMatcher.cs`: pre-traversal exact-path subtree pruning, wildcard folder-name matches, and extension filters. Paths are normalized with component-boundary checks, so excluding `C:\\Temp` does not accidentally exclude `C:\\Templates`.
- `Features/StorageAnalyzer/Services/StorageScanService.cs`: iterative single-level `DirectoryInfo.EnumerateFileSystemInfos` with stack-driven traversal, cached metadata where available, explicit `ReparsePoint` skipping, error accounting and max 24 sampled errors, throttled progress (~250 ms), cancellation returning a labelled partial snapshot, and overlapping-root rejection.
- `Views/StorageAnalyzerWindow.cs`: selected-drive manual Scan, Cancel, hidden/system toggle (enabled by default), opt-in dev-cache preset, user-editable excluded folder names/patterns, file extensions and explicit paths, real counters/progress/error info, and top 15 actual root items (temporary listing until SA2/SA3). Status uses an indeterminate activity bar rather than a fabricated completion percentage. The SA0 **fictional design preview remains explicitly separate** and still does not produce real file/Explorer actions.
- `Tests/StorageAnalyzerScannerSmoke/StorageAnalyzerScannerSmoke.csproj` and `Program.cs`: dependency-free .NET 10 console checks for exact byte and file counts, skipped folder/ext/path behaviour, path-prefix boundaries, bottom-up aggregates, cancellation, progress and overlapping roots.

**UI behaviour and limitations:**
- Opening the window or choosing a drive reads *volume metadata only*. No file enumeration until explicit Scan click. A new scan replaces the previous result; changing drive invalidates previous scan data.
- Drive picker/filters/demo are disabled during scanning, Cancel requests a partial finish; the scan service never mutates files. Closing the window cancels the worker and makes late UI callbacks no-ops.
- "Logical bytes" means the sum of *included readable file lengths*, **not actual allocated disk usage**. Hidden/system items (when deselected), reparse items, explicit exclusions, inaccessible files, ADS and OS-level allocation overhead may make the result differ from used drive capacity. Hard links may be counted more than once when accessible under separate names.
- The current largest-root-items list is a short real size summary for SA1 validation, **not** SA2's zoomable treemap or SA3's full sortable search, details and Explorer integration.
- One drive is selectable in the UI. Options/results support multiple root collections internally, but overlapping roots are rejected and multi-drive UI is deferred to SA4.
- An optional "Exclude development caches" checkbox skips `.git`, `.godot`, `node_modules`, `bin`, `obj` **only when explicitly checked**. All other filters start empty and editable; a user can decide to scan the entire drive or prune irrelevant folders.
- Errors are counted, but only the first 24 messages are stored to bound report memory. When access fails partway through a directory, that directory may be incomplete; warnings and counts are displayed rather than silently implying completeness.

**Validation status: source implemented and pushed; actual Windows/.NET build NOT executed in this session.** Do not mark SA1 accepted until:
1. Run `dotnet build` and `dotnet run --project Tests/StorageAnalyzerScannerSmoke/StorageAnalyzerScannerSmoke.csproj` on the .NET 10 SDK.
2. Check drive selection never starts scanning; Scan does, and Cancel returns a labelled partial result rather than hanging.
3. Scan an *expendable test drive/folder* if available and compare byte totals, excluding `.tmp`, one explicit folder path and a directory-name pattern. SA1 UI currently selects a drive; test service accepts custom roots.
4. Check inaccessible paths, unready/removable drive, root/child reparse points, deep directories, hidden/system toggle, deleted-mid-scan files, repeated scans, closing mid-scan and scrolling/resizing with DPI scaling.
5. Verify SA0 demo remains fictional and Explorer/actions remain absent; other Toolbox windows unchanged.

**Next:** SA2 squarified treemap rendering over the retained `StorageNode` hierarchy, hit-testing, breadcrumbs and zoom. Avoid creating one Avalonia control per file. No NTFS/MFT engine has been added.

## SA2 implementation notes — 2026-11-02

- `Features/StorageAnalyzer/Treemap/TreemapLayout.cs` provides a **renderer-independent squarified treemap** (geometry and hit testing), proportional to the SA1 included logical bytes. Uses bounded O(n log k) top-k selection per displayed directory, with `k=96`. The remaining measurable bytes are consolidated in a truthful `Other (N items)` aggregate tile. Additional subpixel rectangles are combined on narrow viewports rather than rendering impossible-to-hit strips. A zero-size directory displays a friendly empty state.
  - **Historical SA2 first pass:** this immediate-child 96-tile view is retained as a small-layout helper and for the bounded top-15 list, but was superseded for the *real treemap visual* by the 2026-11-03 density pass below.
- `Features/StorageAnalyzer/Controls/StorageTreemapControl.cs` is a single custom-drawn Avalonia `Control` using cached geometry, deterministic tile colours, size labels when space permits, hover highlights and full-path tooltips. No child control per scanned file, no animation timer, no periodic rerender. Geometry recomputes on folder/viewport change; selection/hover repaints only the map.
- `Views/StorageAnalyzerWindow.cs` now shows a **real** coloured treemap after SA1 scanning, with single-click selection, double-click folder drill-down, `ZOOM INTO SELECTED`, `UP ONE LEVEL`, clickable breadcrumb hierarchy (root plus last six when very deep), synchronized selected-file details (name, path, logical bytes, subtree counts, share of current folder) and 15 largest entries from the same focused directory. The short list is bounded and reuses top-k selection; full virtualized browse/sort/search remains SA3.
- An aggregate `Other` tile cannot be individually selected or opened, because its data represents many independent files/folders. Zoom out, resize or use SA3 browser later to inspect its constituents. Zero-byte entries do not occupy treemap area.
- The SA0 fictional demo remains separately labelled and cannot open real folders or call Explorer; scanning remains manual; reparse/exclusion rules and read-only metadata behaviour from SA1 are unchanged. Partial cancelled results are labelled, and the treemap can visualize their known sizes without claiming full coverage.
- The top-15 visible folder entries are cached per focused directory, so selecting tiles does not repeatedly scan/sort that directory's entire child list.
- The existing headless scanner test project now also compiles `TreemapLayout.cs` and checks bounded selection, exact aggregate bytes, proportional area coverage, no overlaps, mouse hit testing, landscape/portrait/tiny viewports and empty cases.
- **Outstanding:** Windows `dotnet build`, `dotnet run --project Tests/StorageAnalyzerScannerSmoke/StorageAnalyzerScannerSmoke.csproj`, and manual UI verification at 980px minimum width, common desktop sizes and 125–200% DPI. Confirm hover/click/double-click/breadcrumb behaviour, partial scans, large-folder responsiveness and contrast. These tests are *not claimed as run in this session*.

**Next SA3:** efficient virtualized directory browser / search and sorting; file-type and category summaries; real file details and guarded Windows Explorer open/reveal/copy-path actions. No cleanup/delete until separately approved.

## SA2-D — Full file-level WinDirStat-style density (2026-11-03)

User tested the earlier SA2 treemap and requested an **all-files view by default**, with an optional customizable minimum size (e.g. 10 KiB / 10 MiB).

- `Features/StorageAnalyzer/Treemap/FileTreemapLayout.cs`: background-safe, UI-independent *nested* squarified layout. Recursively partitions folder rectangles into individual files, using an iterative stack; every **included positive-length file** has its own geometric rectangle, **without the old 96-tile cap or an Other aggregate**. Preserves directory outlines and calculates included directory weights bottom-up, respecting threshold settings without mutating scanned totals/nodes.
- Dense rasterization is performed on the worker into a BGRA byte array and integer pixel hit index. Files smaller than one pixel retain exact geometric records and contribute to the image's dense colour pattern but **cannot all be visually distinguished/clicked at screen resolution**; zooming into a folder expands their pixels. Zero-byte files have no area and are reported separately. Tile colour is stable by file extension.
- `Features/StorageAnalyzer/Controls/StorageTreemapControl.cs`: single cached Avalonia `WriteableBitmap` created after a cancellable background layout task, with efficient per-pixel hit lookup; drawing and hover no longer iterate all files. Bitmap is regenerated only on focus-folder, viewport or threshold change; no animation timer or hundreds of thousands of Controls. Build cancellation and generation IDs prevent stale results from overwriting the latest map.
- `Views/StorageAnalyzerWindow.cs`: adds the **MIN FILE SIZE ON MAP** dropdown (All files default, 1 KiB, 10 KiB, 100 KiB, 1 MiB, 10 MiB, 100 MiB, Custom). Custom accepts a non-negative decimal value and KiB/MiB/GiB unit, then Apply. A map-only filter rebuilds the bitmap from the already-scanned tree; **it does not re-scan, delete, alter or exclude any files from scanner totals**. The map summary reports shown files/bytes and hidden file count/bytes separately.
- Single-click now picks an actual file, double-click enters its **containing folder**; top-15 immediate child folders and breadcrumb navigation are retained. All SA1 safety behaviour, manual scanning/cancellation and the explicitly synthetic preview remain unchanged.
- Headless tests extend `Tests/StorageAnalyzerScannerSmoke` to assert all-file inclusion, nested folder boundaries, zero-byte handling, size thresholds (10 KiB, 1 MiB and above-largest), no mutated scan nodes, dense layout >96 files, cancellation and raster/hit buffer bounds.
- **Known physical limitation:** on a finite-size canvas, 1 KiB among hundreds of gigabytes may have area far smaller than one pixel. Even though all eligible positive-size files have layout records, no screen can show them as distinct separately-clickable shapes simultaneously. Zoom into a subtree or set a minimum size to improve visual legibility. The custom size filter is *display only*. Compare to the earlier SA2 screenshot before approving aesthetics.
- **Validation still pending:** `dotnet build` and `dotnet run --project Tests/StorageAnalyzerScannerSmoke/StorageAnalyzerScannerSmoke.csproj` in .NET 10, plus Windows test on modest and very large scans, hover/double click/drill-down, rapid filter changes, resize, zero-byte-only folder, cancellation and DPI scaling. No local Windows/Avalonia build was run in this session.

The previous section records the *original* 96-child design for history; **SA2-D supersedes that rendering algorithm**, not SA1 storage scanning or the bounded largest-items list. SA3 remains file-browser/type-summary/Explorer integration.

## SA2-D follow-up — omit 0-byte and 1-KiB files from geometry (2026-11-03)

User clarified: **do not draw 0-KiB or 1-KiB files as individual shapes**.
The file-level treemap now enforces a permanent **strictly greater than 1 KiB**
floor (`FileTreemapLayout.MinimumVisibleBytes = 1025`). This applies even
when callers request a threshold of zero or enter a lower custom threshold:
files sized 0–1024 bytes never get `FileTreemapTile`, a bitmap painted
shape, or a file hit-test index. The scan node hierarchy, scanned file count,
and scanned logical byte totals stay untouched.

- The default dropdown now reads **Above 1 KiB**. Larger map-only filters
  remain 10 KiB, 100 KiB, 1 MiB, 10 MiB, 100 MiB, and Custom.
- The effective threshold for the custom input is
  `max(1025, ceil(requested number of bytes))`.
- The treemap summary reports hidden nonzero files and their hidden bytes;
  zero-byte files are reported separately because they occupy no area.
- Geometry and rasterization skip hidden tiny files before layout, reducing
  visual clutter and expensive painting. Larger files still render
  individually with no 96-tile cap.
- Headless smoke tests cover exact sizes 0 B, 1 B, 1024 B and 1025 B,
  custom thresholds below the hard floor, higher filters, unchanged scanner
  totals, and 300 files of 2 KiB to guard against a regression to 96 tiles.
- The prior paragraph referring to **all positive-length files** describes
  the earlier SA2-D version and is superseded by this user-approved floor.
- Windows `dotnet build`, scanner/geometry smoke tests, and visual review
  are still pending; don't mark acceptance before running them.
