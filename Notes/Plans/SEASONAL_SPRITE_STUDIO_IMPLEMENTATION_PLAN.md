# Seasonal Sprite Studio — Implementation Plan

> **STATUS: PLANNED / NO IMPLEMENTATION CODE WRITTEN**
>
> **Created:** 2026-10-23  
> **Repository:** ben0kay/BEN_TOOLBOX_2.0, branch main  
> **Audience:** Codex and future coding assistants  
> **Location:** Notes/Plans/SEASONAL_SPRITE_STUDIO_IMPLEMENTATION_PLAN.md  
> **User-approved winter style:** **snow on the EXISTING LEAFY tree**, not leaf removal, not branch reconstruction.
>
> **Reference workflow:** ChatGPT image generation -> transparent high-resolution PNG -> Seasonal Sprite Studio -> Pixel Art Converter -> Sprite Batch -> Godot/Fantasy 2D project.

## 0. Important handoff: read before editing

1. Read [project README](../../README.md), any current development conventions and this entire plan.
2. Inspect current code, not assumptions:
   - `Services/ToolLauncher.cs`: tool catalog / `Game` category / native-status labels.
   - `Views/MainWindow.axaml.cs`: explicit native tool routing in `LaunchTool`.
   - `Views/PixelArtConverterWindow.cs`: three-column UI, selected-image preview, zoom/pan `PixelViewport`, throttled asynchronous "latest settings win" preview, grouped right-side settings, numeric click-to-edit fields, named Master Configs.
   - `Services/PixelArtService.cs`: ImageSharp image pipeline, alpha, palette, transparent output. **Do not merge seasonal processing into this service.**
   - `Services/PixelArtExportService.cs` and `Services/SpriteBatchService.cs`: existing output planning and renaming patterns, read-only preflight, non-overwrite safety.
   - `Services/PixelPalettePresets.cs`, `Services/PixelArtConfigStore.cs`: local user-data persistence patterns.
   - `BEN_TOOLBOX_2_0.csproj`: .NET 10 / Avalonia 12.1.4 / ImageSharp 3.1.12. Existing ImageSharp security advisories are a **separate tracked compatibility/security task**.
3. Follow the user's preference for modular, comprehensible, data-driven code and **excellent performance**, with simple controls and immediately visible previews. Avoid dozens of tiny classes until justified.
4. **Implement one phase at a time**, commit in small coherent steps, and update the status table/log with exact tested facts. Do not claim build success if only static checks were possible.
5. This is a standalone native tool. Do not break or rework Pixel Art Converter, Sprite Batch or current toolbox navigation merely to add it.

## 1. Product goal and boundaries

Build a **Seasonal Sprite Studio** inside **Game Development** for generating consistent tree, bush and plant variants from one **flattened transparent PNG** without depending on cloud AI or sending each seasonal version back to ChatGPT.

Example: upload one 1254×1254 green oak tree. The editor should identify probable foliage, let the user fix its selection, protect brown trunk/roots, and produce spring, summer, autumn and **snowy leafy winter** sprites with:

- The **exact same canvas width, height, alpha-safe placement and ground/root origin** as source.
- The trunk/roots left **pixel-identical to source** wherever protected and not explicitly overlaid with snow.
- Foliage recolouring applied only through an editable mask; shadows and tonal texture remain recognisable.
- Optional believable snow **resting on upward-facing foliage/branch surfaces**, not a global white tint.
- Consistent named presets, live before/after previews, independent PNG exports and safe original preservation.
- Exported images ready for subsequent pixelation with the *same* Pixel Art Converter master configuration.

**Technical truth:** One source PNG is flattened. An automatically generated "foliage layer" is a **selection mask**, not actual recovered leaf/trunk geometry. Pixels where leaves hide branches cannot be reconstructed. Automatic segmentation can be wrong, especially for brown/red leaves, moss, blossoms, green painted highlights on bark, similar-hued branches, semi-transparent edges and very dark shadows. Manual correction is **not optional** in a usable V1.

### Explicit scope decisions

- **Winter V1 = keep leaves; frost/cold foliage and snow on top.** No bare/dead tree, no leaf removal or invented branches.
- **No AI model, third-party inference cloud, or image-generation API in V1.** Pure deterministic local ImageSharp operations.
- **No existing sprite overwrite by default.** PNG outputs go to explicit destination, with suffixes. Backups/restore, if ever added, must be separately designed.
- Source remains a single flattened RGBA PNG. Support optional externally authored mask PNG if feasible, but don't require layered PSD/Krita documents.
- Mask editing and seasonal effects should work **before** Pixel Art Converter to use all source resolution. Downstream tools should not silently alter the tree canvas/origin.

## 2. Desired end-user workflow

1. Open **Game Development -> Seasonal Sprite Studio**.
2. Drag/drop or browse to a transparent tree PNG. Display original full-res dimensions and file name; zoom, pan, Fit and checkerboard transparency preview.
3. Click **AUTO-DETECT FOLIAGE**. An overlay displays what the detector considers foliage. Brown trunk/root pixels should usually remain excluded.
4. Adjust **Hue Range / Colour Tolerance**, **Saturation Threshold**, **Dark Leaf Inclusion**, **Edge Feather** (where useful). View the actual foliage mask in colour, not merely the final tinted output.
5. Use **Add to Foliage**, **Subtract from Foliage**, **Protect Trunk** or **Erase Mask** brushes with brush size and undo/redo. Click a pixel with **Pick Similar Colours** to include difficult dark leaves or off-green plants.
6. Toggle mask preview: **Original / Mask Overlay / Selected Mask / Seasonal Preview / Before-After**. Any edit is non-destructive.
7. Choose a season from tabs/buttons: **Spring, Summer, Autumn, Winter**. Adjust effect strength and colour mapping. **Winter keeps foliage and places snow on some upward-facing surfaces.**
8. Switch back and forth across seasons without losing the corrected foliage mask. Preview all four using the same selection.
9. Optionally **SAVE PROJECT / MASK** to reuse the selection later; reopening should recover precisely which pixels were selected and protected.
10. **EXPORT SELECTED SEASON** or **EXPORT ALL SEASONS** with names such as `Oak_01_spring.png`, `Oak_01_summer.png`, `Oak_01_autumn.png`, `Oak_01_winter.png`.
11. Pass exported PNGs into Pixel Art Converter for shared pixelation style and then into Sprite Batch for final renaming/resizing. An explicit direct handoff can be added later, not required for the first working generator.

### Single-image first; batches later

P1–P4 prioritize one loaded sprite and its masks. Batch processing is **later** because different tree images typically require different foliage masks and trunk protection. Do not apply the first tree's pixel mask coordinates blindly to a second tree.

## 3. UX/layout target

Use a native resizable Avalonia tool window with the existing dark navy, restrained cyan/amber accents and lightweight controls. Target 1440×900 preferred and smaller widths with scrollable settings rather than clipping.

| Location | Content |
| --- | --- |
| **Left** | Image/file list (single PNG V1, optional queue later), source info, Mask Layers list and mask actions |
| **Centre** | Large transparent checkerboard canvas, pan/zoom, brush cursor, original/season preview, optional split compare and overlay opacity |
| **Right / upper** | Season tabs: Spring / Summer / Autumn / Winter; saved preset controls |
| **Right / masking** | Auto-detect, foliage tolerance, mask strength/feather, protected trunk, brush mode and brush size |
| **Right / effects** | Seasonal palette, colour strength, brightness and variation; winter snow amount/direction/softness/coverage |
| **Bottom** | Export Selected, Export All, output path/naming preview, progress/cancel and any warnings |

Important preferences:

- Like Pixel Art Converter, **numeric settings use a slider PLUS a compact click-to-type value**, no oversized arrow steppers. Selected value visible at narrow sidebar widths.
- Allow individual toggles for **Foliage Mask**, **Protected Trunk Mask**, **Colour Shift**, **Snow Coverage**, **Mask Overlay**. Turning off layers is a diagnostic tool; it should **not destroy** masks or stored season settings.
- Add **Show Original/Compare** and a prominent **Show Mask** toggle so accidental bark recolouring is immediately obvious.
- Provide default presets named by actual season with subtle realism over garish tinting. Warn if foliage detection confidence is poor, instead of claiming AI-like accuracy.
- Separate **editing mode** from panning: mouse wheel to zoom, middle-drag or Space+drag to pan, left-drag edits mask only when a brush is active.
- Keep tree root/anchor marker visible as an optional overlay; optional explicit anchor metadata can be future work.

## 4. Non-destructive mask and layer model

**Core representation (P1+P2):**

- **Source**: immutable original RGBA bitmap. No source mutation. Cache only for selected image during editing; dispose on close/change.
- **FoliageMask**: 8-bit grayscale pixel mask **0..255** aligned 1:1 to full source dimensions. 0 = untouched, 255 = fully affected, values between = soft selection.
- **ProtectionMask**: separate 8-bit grayscale image. Strong protection forces seasonal colour blending toward 0 regardless of foliage score. Brush it over trunk/roots, branches and any important non-leaf areas.
- **SnowMask**: derived *effect coverage* for winter, not permanently baked into FoliageMask. Can be shown/toggled and later manually corrected if needed.
- **Effect layers**: season-specific operations applied by masking and compositing over a **copy of the source**, not destructive edits.

Recommended protection rule for colour grading: effective mask = foliage mask × (1 − protection mask), with both normalized to 0..1. For snow, allow explicit controls to authorize a little snow on *visible upper branch surfaces* if desired; default protected bark remains unchanged.

**Guarantee:** For any pixel outside effective foliage/snow coverage, retain original **RGBA bytes** exactly. Preserve canvas size and coordinate system. Colour effects preserve original alpha unless explicitly authorized for snow additions. Protect against RGB halo contamination at partly transparent leaf edges (premultiplied-alpha/colour-aware operations where appropriate).

**Layer visibility:** Original/source always present, foliage mask overlay and protected mask overlay separate, per-season colour layer and snow layer can be enabled/disabled independently. Checkbox visibility changes should not erase user edits.

### Saving masks/projects

Use a **versioned project manifest** JSON + lossless mask PNGs (or another lossless representation) with identical dimensions:

- Original source path/relative reference, dimensions and optionally SHA-256 content fingerprint.
- Foliage mask and protection mask PNGs, saved **losslessly**.
- Per-season effect settings, palette presets and mask-threshold settings.
- No embedded source image copy by default unless user explicitly chooses portable package/export in a later phase.
- On reopening with changed/missing original source, **show warning and require relink/confirmation**; never silently apply a mismatched mask to another tree.
- Store named global seasonal presets under per-user LocalAppData (parallel to PixelArtConfigStore), but store **per-image mask projects** in a user-chosen workspace/project folder. These are different kinds of persistence.
- Version schema and validate dimensions, source identity, file paths, colour values, slider ranges and size limits before loading.

## 5. Automatic foliage detection (P2) — heuristic, editable, honest

Start with deterministic colour classification appropriate for common **green foliage / brown bark** imagery. Do NOT introduce a heavyweight segmentation neural net as an early requirement.

**Initial pipeline idea:**

1. Decode original RGBA image once on background worker with bounds/cancellation checks.
2. Ignore fully transparent pixels. Weight semi-transparent pixels carefully to avoid fringe leakage.
3. In HSV/HSL or, preferably for matching, perceptual OKLab/OKLCH, assign a foliage-likelihood score for greens: hue band + saturation/chroma + luminance rules. Don't make hard cutoffs the only possible result.
4. Support sliders for **hue range**, **similarity/tolerance**, **dark-leaf inclusion** and optionally **minimum alpha**. Show scores as semi-transparent coloured overlay.
5. Mask cleaning may suppress isolated false positives and feather selected boundaries slightly. Only do this within reasonable connectivity/distance limits and **do not erase thin leaf clusters** by default.
6. "Click to include similar colours" samples source pixels and expands within colour tolerance. A local flood-select version can select a contiguous cluster rather than every pixel of that colour in the image.
7. Trunk protection may start with a **brown bark heuristic**, but provide manual paint/erase correction; automated recognition alone cannot guarantee bark protection.
8. For **blossom trees, red/pink leaves or autumn-coloured source images**, V1 should offer colour picker/manual seed selection and a clear "green detector may not work here" notice. No promise of universal semantic recognition.

**Edits vs regeneration:** Store the raw auto mask and user-added/subtracted corrections **separately**, or maintain another equivalent stable edit representation. If tolerance changes and detector reruns, do not silently wipe manual corrections. Expose "Reset Auto Detection" and a separate explicit "Clear Manual Mask Edits" action.

### Brush editing

- Brush modes **Add Foliage**, **Remove Foliage**, **Protect Trunk**, **Unprotect**, optional Erase.
- Adjustable radius and opacity/strength, visibly scaled for zoom. Mask paint should be smooth and consistent when dragging quickly (interpolate between pointer samples).
- Undo/redo bounded by a reasonable memory budget. Stroke-based/region-delta history preferred; **do not snapshot an entire 4K mask each mouse event**.
- Disabled during long batch/export operations where consistency would break; cancellation and clear progress.
- Selection mask should support direct inspection at original scale and zoom; preserve alpha and boundaries.

## 6. Season-specific treatment

### Spring — fresh growth

- Subtle bright/light greens, optional small yellow-green variations.
- Colour curves/map based on foliage brightness so existing leaf shading and highlights survive.
- Optional blossoms **not necessary in V1** because adding real flower geometry is harder than hue/tone grading.

### Summer — full green

- Closest to source: normal/lush greens, optional vibrancy/depth adjustment.
- A true "Original" strength=0 route should be exactly identical to source pixels.

### Autumn — foliage-only warm palette

- Map bright greens to golden/yellow, midtones to orange, dark greens to burnt-red/brown while preserving **relative luminance and texture**.
- Blend colours under effective foliage mask; do **not** apply a global hue slider to the whole RGBA image.
- User controls: foliage colour strength, warmth, red/yellow distribution, tonal preservation and optional colour variation. Variation should be spatially coherent (leaf clusters/patches), **not independent random RGB noise per pixel**.
- Source trunk remains exact wherever protected/untouched. Smooth edge masking avoids bright halos.

### Winter (V1) — LEAFY TREE WITH SNOW ON TOP

**Nonnegotiable user choice:** Keep the source foliage and branches. The first winter look is a cold/less saturated **leafy** tree with **patchy accumulated snow on upper foliage surfaces**, like snow resting on branches and leaf clusters. Do not remove leaves or reconstruct hidden wood.

**Target controls:**
- Winter foliage desaturation/cool tint (adjustable, mask-only).
- **Snow coverage** 0–100%, **snow strength/opacity**, **patch scale**, optional **snow direction** (default from above), and **random seed** (deterministic).
- **Protect trunk** default ON; optional light snow on visible upward-facing trunk/branches should be a separate opt-in after proof.
- Snow overlay layer enable/disable, Show Snow Mask, compare with original.

**Starter algorithm (no semantic AI):**

1. Derive top-facing candidate surfaces from **foliage mask spatial structure**, boundary transitions and local neighbourhood occupancy. Prefer upper edges/ridges of leaf clumps rather than lower silhouettes, trunk interiors or every green pixel.
2. Weight candidates by upward exposure and foliage confidence; combine with smooth, seeded low-frequency noise for clustered/nonuniform deposits.
3. Expand/smooth small deposits within the foliage support to get realistic resting snow caps. Avoid uniform white tint, random isolated speckles or floating snow outside the tree.
4. Preserve underlying shading with soft off-white/cool-grey highlight and low-opacity blend; distinguish white top highlights from darker frosted undersides. Use **same seed for stable preview and exported output**, even while slider values change.
5. Existing opaque boundaries and alpha preserved by default. Optional small outward snow caps would require explicit new-alpha permission and edge-safety handling—defer until masking is correct.
6. Protected trunk/root pixels remain unmodified by default, and no detached snow on transparent background.
7. For too-flat source artwork, notify user that heuristic caps may not be convincing; enable manual Snow Add/Remove mask brushes in a later refinement.

**Acceptance:** Winter foliage remains present, natural leaf/trunk shapes survive, meaningful snow appears **above** leaf clusters rather than uniformly over every leaf, and output is deterministic across preview and export.

### Shared season controls

- Each season has independent effect settings. Changing autumn saturation should not rewrite winter settings or masks.
- Colour + snow toggles can be turned off individually for evaluation; 0-strength output should match original where no other enabled effect exists.
- All presets remain **data** not per-tree hardcoded IDs.

## 7. Performance and preview architecture

Reuse the **concept**, not the internal coupling, of Pixel Art Converter's throttled "latest settings win" preview and pan/zoom system.

- One selected image decoded at a time, bounded by width/height/pixel limits (starting cap in PixelArtService: 8192 per side / 32 million pixels; reevaluate full-res mask memory budgets, not assume same limit is always cheap).
- Cache the original decode and current masks while open; changes to season strength shouldn't re-read disk or rerun segmentation.
- Separate **mask computation** from **colour/snow rendering**. Adjusting colour should only rerender output; adjusting detection tolerance may recompute auto mask but **not reload source**.
- UI never runs full-resolution detection/filtering on input thread. `Task.Run` receives immutable options and snapshots of editable masks; **do not access Avalonia controls from a background worker**.
- For slider scrubbing target ~100–150ms preview throttle or a single sequential async preview pump. Use cancellation + generation IDs. Only the most recent settings/selection may publish an image.
- Optionally compute a lower-resolution *interactive working preview* first and perform a full-resolution preview after slider settles; **export always full resolution**, with a clear quality indication if lower-res preview is used.
- Avoid GPU/ML dependency for this. ImageSharp is enough for initial masking, recolour and procedural snow. Custom Avalonia drawing for masks/brush/zoom is feasible.
- Dispose images, masks/bitmaps, streams and cancellations; do not keep every variant as full-resolution bitmaps unnecessarily.
- Undo/redo history bounded by byte budget, not unbounded whole-image copies. No idle redraw loop; rerender on inputs only.
- Batch conversion later: process inputs sequentially by default, require per-image confirmed masks or warn loudly when using heuristics, preserve queue/source safety.

## 8. Export and tool-chain integration

**Standalone first:**

- Destination picker, output filename preview and standard suffixes: `oak_01_spring.png`, `oak_01_summer.png`, `oak_01_autumn.png`, `oak_01_winter.png`.
- Export Selected Season / Export All 4, preserving **exact original canvas dimensions** and root/base registration. PNG with alpha.
- Detect naming collisions and existing files **before** writing. No overwrite without a dedicated explicit confirmation policy; safe atomic staged writes and cancellation cleanup.
- Export uses the **same full-res service, masks, seed and options** as final preview. No uncontrolled randomness or surprise extra colour processing.
- Optionally export `foliage_mask.png` and `protection_mask.png` for inspection/reuse.
- Make it possible to load all exported PNGs into **Pixel Art Converter** using its existing Add Files workflow immediately; consider a `SEND ALL TO PIXEL ART CONVERTER` button in P6 only, through a typed intake API + explicit stage/ownership if necessary.
- Longer chain: Seasonal -> Pixel Art Converter -> Sprite Batch -> Godot. Pixel Art Converter already supports a converted-image handoff to Sprite Batch. Do not duplicate that subsystem in Seasonal Studio.
- Set consistent origin/anchor metadata if later introduced; **no pixel shifting/recentering** as part of seasonal generation.

## 9. Suggested code organization

Suggested only; inspect what currently exists before introducing files.

- `Views/SeasonalSpriteStudioWindow.cs`: native window, panels, interaction, selection, export actions.
- `Views/Controls/SeasonalMaskCanvas.cs` (if warranted): pan/zoom, overlays, brush hit coordinates, pointer capture.
- `Services/SeasonalSpriteService.cs`: pure ImageSharp RGBA + masked colour transform + deterministic snow renderer.
- `Services/SeasonalMaskService.cs`: green foliage heuristic, colour sampling, protection mask and morphological cleanup.
- `Models/SeasonalSpriteOptions.cs` (optional compact records) and `Models/SeasonalMaskProject.cs`: options/season state and project schema.
- `Services/SeasonalProjectStore.cs`: mask PNG + JSON manifest persistence, validation.
- `Services/SeasonalExportService.cs`: output plan and safe PNG writes; separate only if export logic grows.
- `Tests/SeasonalSpriteSmoke/`: synthetic image cases and basic performance/cancellation checks.

Separate data processing from Avalonia UI. Keep class count minimal while roles are clear.

## 10. Phased Codex implementation

| Phase | Deliverable | Status | Acceptance before advancing |
| --- | --- | --- | --- |
| **S0 — Baseline & contracts** | Confirm repo, project schemas, pixel/RGBA mask types, output names, performance caps | **NOT STARTED** | Concrete options and non-destructive invariants |
| **S1 — Single-image foliage masks** | PNG import, green detector and mask overlay, trunk protect, pure services + tests | **NOT STARTED** | Correct green-leaf selections; trunk remains protected on synthetic tests |
| **S2 — Mask editing UI** | Native studio window, brush add/subtract/protect, pan/zoom, undo/redo, original/mask toggles | **NOT STARTED** | User can fix mask on one imported tree without changing original |
| **S3 — Spring/Summer/Autumn** | Seasonal grading and per-season sliders, deterministic preview | **NOT STARTED** | All three visually distinct, trunk pixels unchanged, alpha/canvas stable |
| **S4 — Snowy leafy winter** | Frost/colour tint, directional seeded foliage snow, Snow Mask and controls | **NOT STARTED** | Snow sits on upper leaf clusters; leaves retained; protected bark untouched |
| **S5 — Save/Load & safe export** | Persistent mask project, named season presets, four PNG exports, collision/cancel safeguards | **NOT STARTED** | Reopen mask/project intact; four outputs aligned with unchanged originals |
| **S6 — Optional workflow integration** | Batch queue or explicit handoff to Pixel Art Converter, if user accepts S1–S5 | **NOT STARTED** | No mandatory intermediate user export; original Pixel Art Converter works standalone |
| **S7 — Windows/performance/regression** | Build, smoke tests, representative assets, docs & progress updates | **NOT STARTED** | Verified end-to-end app, reasonable preview latency/memory, no regressions |

### S0 — First Codex task

- [ ] Identify source PNG sample fixtures and expected outputs (one leafy oak, one darker charred tree, one blossom/off-green test, one synthetic bark/leaf transparency image). Tests need not commit proprietary user art.
- [ ] Define per-pixel RGBA invariants, grayscale byte mask and protection rule; document whether snow may change alpha (**default no**).
- [ ] Define serializable season options, output schema version and deterministic random seed.
- [ ] Confirm ImageSharp library APIs and pixel-size/memory budgets against the pinned version.
- [ ] Determine image review output/diagnostic mask screenshots for user acceptance.

### S1 — Auto detection + pure tests

- [ ] Load one PNG (read-only); generate green foliage-likelihood mask.
- [ ] Generate initial bark protection / trunk hints conservatively; avoid false guarantees.
- [ ] Show overlay preview of foliage vs protected masks, with sliders for tolerance and include-dark-leaves.
- [ ] Test alpha=0 skip; fully opaque bark vs green leaf; partly transparent edges; dark green foliage; foliage islands; all transparent PNG; no green foliage.
- [ ] Test **protected pixels unchanged byte-for-byte** when seasonal preview has no snow on bark; source file hashes unchanged.
- [ ] Fail cleanly for oversized/malformed files; cancellation and memory cleanup.

### S2 — Brush correction and layer toggles

- [ ] Register `seasonal-sprite-studio` in ToolCatalog with category `Game` and truthful native status; add MainWindow.LaunchTool case.
- [ ] Resizable Studio window with selected-file preview, Fit/zoom/pan and original/mask overlay toggles.
- [ ] Add/Subtract/Protect/Unprotect brushes, size/opacity sliders + editable numeric fields, stable pointer mapping at zoom/pan.
- [ ] Brush stroke interpolation, bounded undo/redo, Reset Auto Detection without wiping manual changes.
- [ ] Test masks retained while switching preview modes and toggling visible layers.

### S3 — Recolour without destroying art

- [ ] Implement masked tone-aware seasonal mappings (no global colour shift).
- [ ] Spring, Summer and Autumn separate settings; presets, live preview, show before/after.
- [ ] Test 0 effect = byte-identical to source; unselected bark exact; shadows retained; no alpha halos; export size same.
- [ ] Validate variation seed determinism, not independent random recolouring flicker.

### S4 — Winter leaves-with-snow

- [ ] Implement top-surface weight from foliage geometry; combine with smooth seeded noise and snow coverage slider.
- [ ] Add winter frost/colour tone, snow strength, patch scale, direction and deterministic seed controls.
- [ ] Snow overlay enable, Show Snow Mask and no-snow toggle; original tree branches/leaves remain.
- [ ] Test snow does not appear in bottom roots or fully transparent voids; protected trunk unaffected.
- [ ] Test seed stable across slider adjustments/export and preview; zero snow coverage = no snow change.
- [ ] Get user screenshot approval with an actual leafy oak before attempting any fancier accumulation or branch effects.

### S5 — Persistence and export

- [ ] Saved mask projects with version, original source identity (dimensions + hash) and mask PNGs.
- [ ] Reopen/restore manual corrections and all seasonal settings; error/relink prompt for mismatched source.
- [ ] Persist named global season presets independently of a particular image mask.
- [ ] Four aligned PNGs from one input, safe suffix naming, output preview, collision checks, no overwrites by default, cancellation and progress.
- [ ] Ensure one season export and all-season export produce byte-identical matching variants for the same input/options.

### S6 — Optional seamless handoff (not blocking S1–S5)

- [ ] Add typed image/file intake API to Pixel Art Converter if still desired and stable; no reflection or synthetic drag/drop.
- [ ] Stage finished seasonal variants in a unique temp session if direct handoff is needed; transfer lifetime ownership, cleanup when receiver no longer needs files.
- [ ] Keep masks and original source ownership separate; do not hand a seasonal mask PNG to the image queue by accident.
- [ ] Retain Pixel Art Converter's independent multi-file and Sprite Batch workflows.
- [ ] If user prefers, defer and rely on standalone export + drag/drop until proven.

### S7 — Validation and regression

- [ ] Run `dotnet restore`, `dotnet build`, `dotnet run`, `Tests/SeasonalSpriteSmoke` and relevant existing PixelArt smoke tests on **Windows**.
- [ ] Manual oak tree: auto detect -> protect root -> change autumn strength -> winter snowfall -> toggle snow off -> export four seasons.
- [ ] Compare the original trunk/roots to variants at identical pixel coordinates; verify mask/protection and unchanged canvas dimensions/transparent border.
- [ ] Test off-green/pink-blossom source and heavily overlapping bark/leaves; ensure brush correction works when auto-detect fails.
- [ ] Test rapid brush strokes, rapid slider scrubbing, resizing/high DPI, selecting other images, close/cancel while tasks are pending.
- [ ] Check memory/CPU during slider scrubbing and idle; ensure no unbounded bitmap copies or repeated full image decode.
- [ ] Verify no source file was modified and no existing destination was overwritten; reopen saved project with/without original present.
- [ ] Update this phase table/README with commits, test output, screenshots and outstanding limitations.

## 11. Quality traps Codex should avoid

- **Don't equate colour mask with semantic layer extraction.** Leaves that overlap trunk pixels will need manual mask corrections. Trunk partly hidden by leaves cannot be recovered.
- **Don't mutate the original.** Every preview/export derives from original + masks + effects.
- **Don't "just shift hue" globally.** It turns bark colours and ruins shading.
- **Don't place winter snow uniformly over entire foliage or randomly in transparent regions.** Accumulation should visibly follow upper-facing shapes with adjustable patchiness.
- **Don't let auto detection reset painted mask edits when a slider changes.** Separate user corrections from generated mask.
- **Don't repaint every pixel each frame or decode every input on slider motion.** Shared immutable snapshots, background work, cancellation and bounded caches.
- **Don't build fake layers using alpha without masks.** Original alpha alone distinguishes transparency, not bark vs leaves.
- **Don't store entire high-res undo bitmap on each pointer move.** Stroke deltas/history budget.
- **Don't fake successful Windows builds/tests.** Commit ≠ compile; user screenshots and smoke tests establish acceptance.
- **Don't enable destructive overwrite when applying a saved season preset.** Project masks and presets should never control external file deletion.

## 12. Extra ideas (after core S1–S5)

- **Summer rain/wet sheen** mask-based darkening with small specular highlights; separate from snow.
- **Autumn progression slider** 0–100%: patch-by-patch transition green -> yellow -> orange -> red rather than all leaves changing at once.
- **Leaf-drop coverage** (without reconstructing trunk) for stylized thinning only; truly leafless winter version remains a separate image or manual branches.
- **Blossom/petal colour variants** via user-seeded similar-colour selection for pink/white trees.
- **Side-by-side seasonal grid** and animated scrub through months to compare biome cohesion.
- **Batch recipe across multiple sprites** after every image has an approved saved foliage/trunk mask.
- **Import externally-authored foliage mask** from graphics editor; supports artists who can separate layers outside Toolbox.
- **Seasonal project export/import** portable bundle with source and masks, optional later.
- **Shared downstream tool action**: Send four seasonal sprites directly to Pixel Art Converter with one selected master pixelation preset; then Pixel Art Converter -> Sprite Batch.
- **Godot metadata helper** to emit matching sprite origins and filenames, not a full resource rewrite.

## 13. Progress / handoff log

- **2026-10-23 — Plan created.** Winter style confirmed as leafy foliage carrying snow rather than bare branches. Verified existing Avalonia native tool catalog/routing, Pixel Art Converter preview/persistence/ImageSharp services and NOTES/Plans folder. Specified heuristic mask + manual correction, non-destructive trunk preservation, season-specific colour shifts and directional snow, shared-canvas export, optional downstream handoff and performance/testing rules. **NO APP CODE CHANGED. S0–S7 NOT STARTED.**

**Next Codex instruction:** Implement **S0 and S1 only** first: agree on pixel-mask contracts, build an offline green foliage detector / trunk protection heuristic and synthetic smoke tests. Provide a mask overlay screenshot for one real tree image. Follow with **S2** manual brush correction and toggleable layers before attempting autumn/snow rendering.
