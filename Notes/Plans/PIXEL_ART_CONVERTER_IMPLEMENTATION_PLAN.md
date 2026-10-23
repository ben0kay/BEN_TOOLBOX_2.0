# Pixel Art Converter — Avalonia Implementation Plan

> **Status: P0 specified; P2 preview coded, Windows validation pending. P1 processing dependency partially coded; P3/P4 coded, Windows validation pending. P5 pending.**
> **Created: 2026-10-23**  
> **Location:** Notes/Plans/PIXEL_ART_CONVERTER_IMPLEMENTATION_PLAN.md  
> **Audience:** Codex and future development assistants  
> **Goal:** Native Game Development tool with live selected-file conversion, its own export and a direct handoff to Sprite Batch.

## 0. Before coding

Read the root [README](../../README.md), this plan and the actual latest source. Do not assume this feature already exists.

Relevant repository files, inspected on 2026-10-23:

- Services/ToolLauncher.cs — ToolCatalog has a Game category and allowlisted tool IDs. Register a native Pixel Art Converter entry here.
- Views/MainWindow.axaml.cs — LaunchTool selects native tool windows by ID. Add the new tool route here.
- Views/SpriteBatchWindow.cs — existing native Sprite Batch window has drag/drop, droppedFiles list, PREVIEW, EXPORT, scale, prefix, numbering and RAW options. It currently exposes no public preloaded-input interface.
- Services/SpriteBatchService.cs — SpriteBatchOptions.SelectedFiles is an optional array of PNG *paths*. Plan and export use Image.Identify/Image.Load on those paths. Its scaling currently uses KnownResamplers.Bicubic.
- Views/SpriteSplitterWindow.cs / Services/SpriteSplitService.cs — existing transparent PNG crop/split workflow. Preserve its standalone behaviour.
- BEN_TOOLBOX_2_0.csproj — .NET 10, Avalonia 12.1.4, SixLabors.ImageSharp 3.1.12. Note ImageSharp advisories; address dependency upgrades in separate tested work.

**Do not edit game repositories, rebuild the dashboard or modify unrelated tools.** Keep P0–P5 statuses and this plan's log honest. A GitHub commit is not evidence of a successful Windows build.

## Updated conversion contract — 2026-10-23

User clarification supersedes the original logical-resolution export design below: a 1000×1000 source at detail 64 is reduced to a 64×64 logical grid and enlarged back to **1000×1000 with nearest-neighbour**. Source canvas dimensions and alpha remain intact. Non-square grids follow source aspect ratio. Logical width is capped at source width. This is pixelation; palette quantization is a separate deferred option. All queued files share one setting; only selected preview is processed.

P0 and P2 requested first; the minimum P1 processor needed for actual previews is included. Current UI: append/deduplicate PNG paths, auto-select first, select another with shared settings, remove/clear, integer slider 16–512 and 32/64/96/128/256 presets, box/nearest source sampling, checkerboard, sharp fit preview, debounce and serialized latest-request-wins processing. Input limits: 8192 per side / 32 million pixels. Invalid image content reports a selected-preview error rather than a successful fake image. No eager thumbnail decoding, export or Batch handoff yet.

Historical design below is retained for context; any instruction saying exported dimensions equal logical grid dimensions is **superseded** by this contract. P3/P4 must use the same original-canvas processing pipeline.

## 1. User-approved workflow

1. Open **Game Development → Pixel Art Converter** in a native Avalonia window.
2. Drag/drop multiple PNGs from Explorer, or use Add Files. Additions append rather than replace; deduplicate identical paths.
3. **The first valid image is automatically selected.** It appears in the large preview.
4. Drag the **Pixel Width / Detail** slider: the preview regenerates interactively. Smaller width gives chunkier pixels.
5. Click a different file in the left queue: its converted preview is shown, using the current shared settings. No batch-wide processing on every slider change.
6. From the converter choose:
   - **Export Selected / Export All** to a chosen folder with basic rename controls; OR
   - **Send All to Sprite Batch**. This is a real transfer of the **already-pixelated** versions to Sprite Batch, which opens preloaded. User can then resize/rename and choose the **final** export destination in Sprite Batch. **No converter export to a final destination is required before handoff.**

Examples: 20 transparent, hand-painted 1254x1254 tree PNGs → view one at logical 128x128, try 64/256, select other trees, then export all or send converted variants to Sprite Batch.

**Important:** This is deterministic, local image processing, NOT AI redraw. A downsampled photograph will not automatically become hand-placed professional pixel art.

## 2. UI layout and controls

Use the existing BEN TOOLBOX dark navy/cyan styling, in a resizable standalone window.

| Region | Controls | Required interaction |
| --- | --- | --- |
| Left | Drop PNGs, Add Files, Remove Selected, Clear, file/thumbnail list and count | First valid file autoselects; clicking another changes preview. Keep insertion order, avoid duplicate paths. |
| Centre | Large checkerboard **converted preview**, optional Original/Converted toggle, zoom/Fit, filename and dimensions | Selected image only; nearest-neighbour **display** interpolation. Checkerboard is preview-only, never baked into PNG. |
| Right | Pixel Width slider and numeric value, presets 32/64/128/256, resample, preserve alpha/aspect, optional palette settings | Changes regenerate selected preview after brief debounce. Defaults apply to every queued file in V1. |
| Bottom | Export Selected, Export All, Send All to Sprite Batch, Cancel, status/progress | Export preview/naming before writing; clear processing/handoff state and errors. |

### Pixel Width / Detail — one unambiguous main slider

The main slider defines **logical pixel output width**, NOT a generic blur percentage or a second "pixel block size" control. Initial target range: 16–512 integer pixels, with presets 32/64/128/256. Default 128 for testing, adjustable.

- Source 1254x1254 → Width 128 yields logical output **128x128**.
- Source 1200x800 → Width 128 + Preserve Aspect yields ~**128x85**, using defined rounding.
- Lower value = chunkier pixels; higher value = finer detail.
- Optional Square Canvas "Contain" mode pads into a transparent square without stretching; base/bottom anchor for trees can be a later setting.
- The preview *looks large* because the output is magnified using nearest-neighbour; the file resolution remains the logical result.
- Do **not** add a competing output-size slider in V1. A later optional 1x/2x/4x nearest-neighbour export multiplier may produce larger crispy images without changing pixel detail.

### Core conversion options

- **Source resample:** Area/Average (good for reducing detailed hand-painted art); Nearest (good for art that is already pixelated). These differ from the preview's always-sharp magnification.
- **Preserve alpha/transparency** always ON by default, especially for trees and plants. Do not composite onto a black or checkerboard background.
- **Preserve aspect** ON by default; no distorted trees.
- Optional **palette limit** (Original / 16 / 32 / 64), ideally quantize *after* downsampling. If high-quality, deterministic palette reduction is too large for V1, defer to P2b; do not show a dummy setting.
- Optional dithering, contrast tuning, edge cleanup are later improvements.

## 3. The critical Sprite Batch integration design

### Existing reality

Sprite Batch currently receives arrays of **PNG file paths** (SpriteBatchOptions.SelectedFiles), reads images from disk, and rescales with **Bicubic**. It cannot accept a converted ImageSharp bitmap or Avalonia Bitmap directly. Simply opening a new empty Sprite Batch window, or passing the original source PNGs, would **not** satisfy the requested workflow.

### Recommended P4 V1 solution: temporary converted-PNG staging with ownership

1. User clicks Send All to Sprite Batch; snapshot queue paths and immutable settings on UI thread.
2. Convert all files with **the same PixelArtService** as preview/export into a unique private temporary folder, e.g. system temp/BEN_TOOLBOX_2_0/pixel-art-handoff/{unique-job-id}/. This creates *intermediate files only*, **not final user exports**.
3. Use safe collision-proof staged names and retain mapping to original names. Show progress and cancellation. Never overwrite user files or use a fixed globally shared temp directory.
4. Add a **typed preloaded-input API** to SpriteBatchWindow (constructor or public LoadSelectedFiles), accepting the staged file paths and an owned disposable handoff lease. **Do not fake drag/drop events, use reflection or access private controls from another class.**
5. Open Sprite Batch with the converted PNGs selected and clearly labelled **From Pixel Art Converter**. User then chooses destination, naming, resize settings and uses the existing PREVIEW/EXPORT mechanism.
6. Extend SpriteBatchOptions/Service with a resample choice **Bicubic / Nearest**. Preserve Bicubic as the default for existing standalone Sprite Batch use, but select **Nearest** on converted-pixel-art handoffs. Otherwise resizing will blur the new pixels.
7. Ownership of a temporary-session lease transfers to the receiving Sprite Batch window only on successful intake. Staged files must persist while Batch is open or exporting, even if converter closes. Delete only the session's temp folder after Batch closes **and all jobs finish**, or after a failed/cancelled handoff. Handle cleanup failures honestly.
8. A handed-off session is a **snapshot**: later slider changes must not silently alter Batch inputs.
9. Current Sprite Batch **Archive Originals / RAW** means archiving its staged input files, which are **converted PNGs, not the original art sources**. Disable it by default on handoff and explain this. True-original archiving requires source mapping and is later.
10. If user cancels before opening Batch, clean the session. Do not delete source images or final output paths. Test cleanup after cancellation and when one of the windows closes first.

**Longer-term alternative:** refactor both tools around memory streams or a shared image-input abstraction to avoid temp files. This is more invasive to current Sprite Batch; **do not require it for V1**.

## 4. Pixel Art Converter's own export and rename

Standalone output is equally important as handoff.

- Export Selected / Export All to an existing user-chosen destination. PNG only in V1.
- Rename mode A: preserve original name with suffix, e.g. LushTree_px128.png.
- Rename mode B: prefix + sequence, e.g. Tree_001.png, Tree_002.png; prefix/start/digit count.
- Display **final target filenames** in a dry-run output plan, in deterministic queue order, before export.
- Detect duplicate stems (from different input folders), case-insensitive collisions, and pre-existing destination files. **Do not overwrite** by default; offer a clear error/skip policy. Do not modify originals.
- Export should be cancellable, with progress per image and a bounded memory footprint. Avoid corrupt incomplete PNGs: stage write securely and finalize with create-new semantics. Only clean up files created by the current job.
- Capture selected files, output folder, naming and processing settings on **Avalonia UI thread before Task.Run**. Previous Sprite Splitter needed a fix for cross-thread access; do not repeat it.

## 5. Technical architecture / responsiveness

**Suggested files** (names flexible; avoid extra files with no clear responsibility):

- Views/PixelArtConverterWindow.cs — native window, UI events and selected-file preview management.
- Services/PixelArtService.cs — pure ImageSharp processor, resize/palette/alpha, no Avalonia controls.
- Models/PixelArtOptions.cs — immutable validated settings/record, shared by preview, export and handoff. A small record in the service is also acceptable for V1.
- Services/PixelArtExportService.cs — optional if filename planning/transactional writes become substantial.
- Services/PixelArtHandoffLease.cs — scoped temp session lifecycle for P4.
- Tests/PixelArtSmoke/ — minimal independent service smoke tests and fixtures.

**Preview and resource rules:**

1. Validate imported .png file paths, use insertion order and deduplicate by normalized absolute path. Record unreadable/invalid input failures per file. No recursive scan by default.
2. Only convert **the selected item** for live preview. Do NOT run conversion for every dropped image every time the slider moves.
3. Each preview request snapshots immutable options and selected source path, runs asynchronously off UI thread, **debounced ~100–150ms** and cancellable. Use a monotonically increasing generation/request ID to ignore obsolete completions after fast slider changes or selection switches.
4. Avoid concurrent unbounded preview tasks. Preserve UI responsiveness; display a small busy indication and don't overwrite current selected preview with a stale response.
5. Maintain at most 1–2 bounded decoded source/preview buffers, with clear bitmap and stream disposal. Do not decode all 20+ large PNGs at startup. Avalonia preview bitmap creation/assignment must be safely dispatched.
6. Same PixelArtService algorithm for live preview, exported output and handoff staging; no mismatch between preview and saved pixel results.
7. Use image/size limits, validate alpha and output dimensions, report errors, and never generate fake successful previews on corrupted files.
8. Export/handoff process files sequentially (or bounded limited parallelism after profiling), with cancellation/progress and no image buffers accumulating.
9. Use existing ImageSharp resamplers compatible with the project's pinned version; no new chart/UI framework and no network requests.
10. Existing controls must keep working: Sprite Batch can still run in folder/drag/drop modes standalone, and Sprite Splitter unaffected.

## 6. Implementation phases for Codex

| Phase | Work | Status | Acceptance gate |
| --- | --- | --- | --- |
| **P0 — Investigate & contracts** | Read actual code, agree width/alpha/resampler conventions, queue/options contracts | **SPECIFIED; see updated contract** | Behaviour specified, source API compatibility confirmed |
| **P1 — Core processor** | ImageSharp deterministic pixelation, aspect/alpha and tests | **NOT STARTED** | 1254-square to 128-square, non-square and alpha cases correct |
| **P2 — Multi-file live UI** | Register native tool; drag/drop/add; auto-select first; click-to-preview; debounced pixel slider | **CODED; Windows validation pending** | Selected image updates smoothly; no conversion of all files on each change |
| **P3 — Own export/rename** | Destination, naming plan, Export Selected/All, progress/cancel, no overwrite | **CODED; Windows validation pending** | Correct PNGs and filenames; source unchanged |
| **P4 — Sprite Batch handoff** | Temp staging lease, preloaded receiver, nearest resample option | **CODED; Windows validation pending** | Converted images already in Batch without previous final export; resize/rename once |
| **P5 — Regression & docs** | Tests, performance, Windows UI, README/plan progress | **NOT STARTED** | Reproducible end-to-end workflow and tests |

### P0 / P1 — processing proof

- [ ] Define PixelArtOptions as immutable; settings include logical width, aspect/canvas mode, resample and functional palette if implemented.
- [ ] Test 1254x1254 → 128x128; 1200x800 → approximately 128x85; 64, 256 widths; alpha=0 pixels, antialiased edges, all-transparent and mixed artwork.
- [ ] Ensure no crop/stretch unless requested; square canvas means transparent padding.
- [ ] Test cancellation and unsafe/huge dimensions; verify originals' file hashes unchanged.
- [ ] Confirm palette algorithm if included: transparent pixels excluded from palette estimation and no unexpected dark halos.

### P2 — live UI proof

- [ ] ToolCatalog ID pixel-art-converter in Game, routed to native PixelArtConverterWindow by MainWindow.LaunchTool.
- [ ] Register correct native status; existing catalog mislabels several native tools, but avoid unrelated cleanup.
- [ ] Drag/drop/add multiple images; first selected; selection changes preview; remove and clear; valid empty/error states.
- [ ] Pixel width slider + numeric + presets, immediate visual feedback using latest-request-wins and 100–150ms debounce; preview and displayed dimensions correct.
- [ ] Checkerboard transparency and nearest-display scaling; original/converted toggle if feasible.
- [ ] No sluggish UI under 20+ images, no invalid Avalonia worker-thread access.

### P3 — export proof

- [ ] Filename plan/suffix or sequential naming, per-file input/output dimensional readout and collision check.
- [ ] Export Selected and Export All; cancel/report progress; preserve original file data; no silent overwrite.
- [ ] Rendered PNG pixels match selected preview pipeline.

### P4 — handoff proof

- [ ] Click Send All without converter destination; stage converted images to private temp directory, display progress.
- [ ] Typed SpriteBatchWindow intake opens with those converted images selected and clear status. The button **must not open an empty Batch window**.
- [ ] Batch resize/rename/export works; nearest-neighbour chosen for converted inputs; old standalone Batch uses Bicubic unless user changes it.
- [ ] Temp lifetime survives converter closing first; cleanup after Batch finishes/closes. Interrupted/cancelled staging cleans up its own files.
- [ ] Clarify RAW archive behaviour; do not misrepresent staged converted copies as original inputs.
- [ ] No extra export step needed; final user destination selected in Batch.

### P5 — test and handoff

- [ ] Run dotnet restore, dotnet build, dotnet run and service tests on Windows/.NET 10.
- [ ] Manual: drop 4 tree PNGs; first selected; drag slider 256 → 128 → 64; click another tree; preview changes; export all.
- [ ] Manual: drop 20 trees; skip converter export; click Send All to Sprite Batch; preview and change scale/prefix, export once; confirm nearest scaling, transparency and originals untouched.
- [ ] Test 20–100 mixed sizes, duplicate stems, wrong formats, rapid selection/slider changes, cancellation, destination collision, window close mid-task, and DPI.
- [ ] Profile idle CPU, peak conversion memory, hanging temp files, preview bitmap lifecycle. No unbounded per-image cached bitmaps.
- [ ] Update README and this plan's phase statuses/commits; note what *actually* passed Windows testing rather than inferring success from GitHub.

## 7. Extra ideas (proposals, NOT blockers)

| Idea | Why | When |
| --- | --- | --- |
| **Original ↔ Pixel toggle / split compare** | Judge conversions precisely | High priority, include with P2 if simple |
| **Named style presets** (Painterly 128, Sharp 64, etc.) | Consistency across many vegetation PNGs | After V1 |
| **Palette control** (16/32/64/custom) | Reduce inconsistent colours across pixel-art sets | After stable processor |
| **Dithering** (Off / Bayer / Floyd-Steinberg) | More sophisticated gradient-to-pixel look | Later |
| **Alpha fringe cleanup** | Reduce halos at leaf/stem edges | Later |
| **Bottom-anchor transparent canvas** | Align tree trunks and plant roots across equal PNG canvases | After V1 |
| **Pixel grid & zoom lock** | Inspect exact low-resolution details | Later |
| **Per-file option overrides** | Let one tree have different detail from others | Later |
| **Nearest 1x/2x/4x PNG enlargement** | Preserve crisp pixels at larger export dimensions | Later |
| **Sprite Splitter → Pixel Art handoff** | Full sheet → split → pixelate → batch chain | Later |
| **Godot texture-filter tips** | Avoid blurry pixel art after Godot import | Later |

**Recommendation:** Deliver P1–P4 fully before adding advanced stylization. Before/after compare, repeatable presets and transparent bottom-anchor will be most useful for trees and plants.

## 8. Risks and guardrails

- Pixelation is not a generative-art redraw; be explicit about realistic results.
- Keep checkerboard preview UI separate from PNG alpha; do not accidentally export checkerboard.
- Existing Sprite Batch uses Bicubic. Converted pixel art should default to Nearest on batch handoff.
- Background export must use immutable snapshots, not access TextBoxes/ComboBoxes inside Task.Run.
- Temp session folder belongs to a single handoff lease. Clean only files owned by it after the receiver releases them.
- Export is no-overwrite by default, deterministic naming and preflight collision detection.
- Preserve existing independent Sprite Batch / Sprite Splitter usage. No optional feature should force a large refactor.
- Do not change ImageSharp 3.1.12 without dedicated dependency safety/compatibility work.

## 9. Progress log

- **2026-10-23 — Plan created.** Reviewed current Toolbox navigation, ToolCatalog, Sprite Batch UI and path-based service, Sprite Splitter and .NET/ImageSharp project references. Requirements confirmed: multi-PNG queue, auto-select first, selected-file live pixel width slider, selected row changes preview, direct Converter export/rename **and** converted-image pre-final-output handoff to Sprite Batch. **No implementation code changed. P0–P5 not started.**

**Next Codex task:** Start with P0 and P1. Implement pure ImageSharp conversion and unit/smoke tests; then move to P2 interactive UI, P3 native exporter and P4 typed temporary handoff. Implement in small, tested commits.

- **2026-10-23 — P0/P2 preview implementation:** Updated user contract to retain original canvas size; added core processing dependency and native queued live preview. No .NET SDK in implementation environment, so build, service execution and Windows visual testing remain unverified. P1 full test gate remains pending; P3/P4 not implemented.

Service smoke check (written, not executed here): `dotnet run --project Tests/PixelArtSmoke/PixelArtSmoke.csproj`. Checks retained canvas sizes at all five presets, hard pixel blocks, transparent non-square source, unchanged source hash, and cancellation.

### P2 colour and preview refinement — 2026-10-23

Coded: Original, automatic 16/32/64 (deterministic alpha-weighted median-cut), Endesga 32 and grayscale palettes; RGB/Oklab colour matching; brightness/contrast/saturation (0–2); optional Bayer 4×4 dithering (0–1, default 0, only with palette mapping). Palette mapping retains alpha and excludes fully transparent pixels from automatic palette estimation. Automatic palettes are derived separately for each image under the shared settings; a locked cross-image automatic palette is not implemented.

Preview uses the processed logical bitmap directly with Fit and 1/2/3/4/6/8× zoom, scrollable when enlarged. These zoom factors use Avalonia device-independent units; display scaling can affect physical-pixel alignment. Conversion's default full-canvas result still preserves source dimensions. Preview changes use the existing serialized, debounced latest-request-wins pipeline. Palette and adjustment work happens on the logical grid, avoiding full-resolution per-pixel colour matching.

Smoke coverage expanded for deterministic palette conversion, palette limits, and logical preview dimensions. Build/test execution and Windows visual validation remain pending: no .NET SDK available in the implementation environment. P3 export and P4 handoff are still pending.

### Preview navigation refinement — 2026-10-23

Replaced zoom dropdown with pointer-centred mouse-wheel zoom and left/middle-button drag panning. Double-click or Fit Preview resets framing. Nearest-neighbour image rendering retained. Zoom/pan affects only the viewport, never logical detail or output settings. Uses a clipped custom drawing control without reprocessing image data during navigation. Windows build and pointer-interaction testing pending.

### Custom palette editor — 2026-10-23

Coded: Edit/Create Palette opens an editor seeded from Endesga 32, the custom draft, or a selected saved palette. Clickable numbered swatches, RGB sliders and validated six-digit hex entry publish copied palette snapshots into the selected preview. Save as New Preset stores named palette-only presets under LocalApplicationData/BEN_TOOLBOX_2_0/PixelPalettes; presets reload into the palette dropdown on subsequent launches. Built-in Endesga data is never mutated. Existing names are rejected; use a new name for revisions. Presets contain palette colours only, not image paths or all converter settings. Source alpha is preserved by custom colour mapping. Storage uses temporary writes and rename; malformed saved files are reported. Editor changes remain an unsaved draft until saved. Original/automatic modes start a new editor from Endesga; automatic-palette extraction into the editor remains future work.

Added smoke coverage for custom palette mapping, alpha, invalid hex rejection and built-in colour count. .NET SDK is unavailable here; build, test execution, saved-preset restart, and Windows UI validation remain pending. P3 export and P4 handoff remain pending.

### P3/P4 export and Sprite Batch handoff — 2026-10-23

Coded: Converter output folder picker, selected-only/all scope, original-name `_pxWIDTH` suffix or prefix/start/digit numbering, filename/dimension dry-run list, and explicit Preview Output before Export. Options and source queue are snapshotted on the UI thread. Export reruns conflict checks, preserves source canvas and alpha through the shared processing pipeline, writes temporary PNG data then moves without overwrite, and rolls back this job’s finalized files on failure/cancellation. Duplicate stems/targets and existing destinations are errors; no silent skip or overwrite. Preview Selected scope does not affect Send All.

Send All stages a converted snapshot into a unique private temp session and opens Sprite Batch via typed LoadPixelArt API. No converter output destination or prior final export is needed. Staged names contain queue ordinals plus original names to handle duplicates and preserve order. The receiving Batch window owns the lease, is independent of the converter’s window lifetime, and releases temporary data after close and any active Batch job finishes. Failed/cancelled staging cleans its own session. Cleanup failures are reported/logged; process termination can leave a temporary session behind (startup crash-recovery cleanup is not implemented).

Sprite Batch adds a nearest-neighbour option, automatically selected on handoff. Standalone default remains bicubic. Scale range now supports >0 through 8×, bounded by 8192 pixels per side / 32 million pixels for input and output. RAW archive is disabled for handoff windows because staged inputs are converted images, not original art. Final destination/naming/scale are selected in Batch as usual, followed by its Preview and Export.

Service smoke coverage now includes export canvas/alpha, existing-output protection, duplicate stems, multi-file staging/cleanup, and a Batch resize/export from staged inputs. Source/static checks passed (`git diff --check`). No .NET SDK is installed here: build, smoke execution, Windows UI, cancellation/window-close races and full end-to-end visual comparison remain pending under P5.

Windows verification commands:

```powershell
dotnet build
dotnet run --project Tests/PixelArtSmoke/PixelArtSmoke.csproj
dotnet run --project BEN_TOOLBOX_2_0.csproj
```

Manual: export four PNGs with suffix naming; try a conflicting filename; change naming/detail after planning and confirm replanning is required. Send all without choosing a converter output folder; close converter; finish Batch export and verify pixels/alpha, then close Batch. Also cancel staging/export and close Batch during a job; confirm source files remain untouched and no owned temporary session remains after graceful cleanup.

### Folder processing mode — 2026-10-23

Coded: Manual files remains the default with its own queue. Folder processing has a separate queue and folder picker, optional recursive scan, relative-path list, existing shared selected preview/settings, save-copy export and optional overwrite. Mode switches retain each queue and invalidate output plans. Manual drag/drop remains manual-only. Scans are cancellable/off-thread, skip links/inaccessible entries and `.pixel-backups`, and cap at 10,000 PNGs / 100,000 visited folders. The folder scan lists paths without decoding every image. Image validation happens on preview/output planning.

Folder save-copy exports preserve relative subfolders in the chosen destination. Overwrite is folder-mode-only, ignores destination/rename options, shows full affected paths and count in Preview Output, and requires the explicitly labelled Overwrite action. Selected-only scope remains available. Processing options and paths are snapshotted; every image is converted into a same-folder temporary file before any source replacement. SHA-256 checks detect changes during staging/before replacement. Each atomic File.Replace retains the original in `source-directory/.pixel-backups/{timestamp-job-id}/{original-name}.bak`; these backups remain after success and rollback. Ordinary cancellation/failure restores replaced sources from backups. A process crash or power loss can interrupt the group; backups are retained for manual recovery. There is no automatic restore UI yet. Fully atomic multi-file filesystem transactions are not claimed.

Input canvas size, alpha, filenames and source directory locations are retained on overwrite. Changing a previously coarse 128-pixel image to 256 cannot recreate lost detail; original source artwork gives the best result. Save-copy remains the default. Tests added for flat/recursive scans, overwrite plan locations, backup integrity, backup exclusion and rollback on cancellation after a replacement. Test execution and Windows File.Replace/UI checks remain pending because the implementation environment has no .NET SDK.
