# BTX Storage Analyzer — Implementation Plan (SA0–SA4)

> Repository: BEN_TOOLBOX_2.0 · Category: **Files / File Toolbox**
> Updated: 2026-11-02
> Status: **SA0 CODED AND PUSHED TO main; WINDOWS BUILD AND VISUAL ACCEPTANCE PENDING. SA1–SA4 NOT STARTED.**
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
| SA1 | Real, cancellable, read-only metadata scanner for one selected drive, pre-scan exclusions, visible progress/errors | Not started |
| SA2 | Efficient real treemap geometry and hit-testing, zoom/drill-down, tooltips and synchronized selection | Not started |
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
