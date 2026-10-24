# Seasonal Sprite Studio — Hybrid Avalonia + Godot Implementation Plan (V2)

> **Current BEN TOOLBOX standalone-first execution notes:** [Seasonal Sprite Studio implementation notes](SEASONAL_SPRITE_STUDIO_IMPLEMENTATION_NOTES.md), phases A0–A5. User has chosen manual file loading/export and separate tools **before** automation. This V2 document remains the longer-term Avalonia↔Godot integration plan; do not interpret its suggested Godot-first sequencing as a prerequisite for testing the standalone Studio.
>
> **STATUS: Toolbox A0–A3 now implemented; .NET/service/headless checks pass, Windows visual acceptance pending. Godot phases and baked outputs remain planned.** Updated 2026-10-23.
> **This V2 replaces the previous Studio-first four-baked-PNG implementation plan.**  
> **Scope:** BEN TOOLBOX Avalonia asset authoring + Fantasy 2D Godot dynamic seasons. Codex must receive explicit permission for each repo/phase.  
> **Winter V1:** Leaves remain present, with existing Godot snow effects on top; no leaf removal or hidden-branch reconstruction.

## 1. Architectural decision — read first

Build two **complementary tools**, not two independent seasonal engines.

| Responsibility | Primary system |
| --- | --- |
| Runtime season state, transition strength, in-game green/yellow/orange colouring | **Godot** shared season state + vegetation shader |
| Runtime snow on leafy trees, rain wetness, wind sway, shadow interactions | **Existing Godot environment shaders/adapters**, extended safely |
| Foliage/trunk pixel selection, mask painting, editable mask projects | **BEN TOOLBOX Seasonal Sprite Studio** |
| One authored mask PNG per final tree artwork variant, optional runtime mask | **Studio exports; Godot consumes** |
| Optional offline spring/summer/autumn/winter previews and baked PNG exports | **Studio**, secondary purpose |
| Pixelation, resizing, renaming | Existing **Pixel Art Converter** / **Sprite Batch** tools |

**Answer to sequencing:** Do NOT wait for a full Godot season/calendar system before starting the Studio. Agree on one **shared foliage-mask format** first, make a **small Godot shader tint prototype**, then implement Studio mask generation/painting and connect its output. A calendar/biome seasons manager comes later. If the user is focusing on the Toolbox, Studio mask work may begin after the shared contract, even if the Godot prototype is not finished.

**Do not reduce the Studio to only a mask exporter**: keep its visual seasonal preview, named presets, protected trunk editing, non-destructive layered editing, and optional four-sprite export. However, **Godot is authoritative for the appearance and timing of runtime dynamic seasons**, especially winter snowfall.

## 2. Current code the Codex agent MUST read

**Godot repository:** ben0kay/isometric-fantasy-2d

- `README.md` and `CodePreferences.md`: mandatory architecture/performance conventions and one scoped commit per job.
- `ENVIRONMENT/Seasons/README.md`, `ENVIRONMENT/Seasons/Definitions/README.md`: currently **future placeholders**, not implemented runtime seasons.
- `ENVIRONMENT/Wind/Visuals/VegetationSway.gdshader`: wind-deformed source `sample_uv`, `shadow_only` branch, final `COLOR = pixel * sprite_tint * inside;` anchor.
- `WORLD/Content/Vegetation/Shared/Snow/VegetationSnow.gdshaderinc` and `VegetationSnowAdapter.cs`: snow include injected into wind shader, green/pink foliage heuristic, canopy/noise snow, cached shared material variants, change-driven snow coverage.
- `WORLD/Content/Vegetation/Shared/Wetness/VegetationWetness.gdshaderinc`, `VegetationWetness.cs`, `FoliageMaterialLinks.cs`: wet-leaf include composed with wind before snow; wet uniforms forwarded to snow variants.
- `WORLD/Content/Vegetation/Trees/Shared/TreeDefinition.cs`, `TreeVisualVariant.cs`, `Tree.cs`: per-tree species foliage profile, per-**artwork variant** texture/anchor/materials, snow metadata and shared tree root.
- `ENVIRONMENT/Weather/Snow/WorldSnow.cs`: per-chunk material lifecycle and efficient settled snow.

**Avalonia Toolbox repository:** ben0kay/BEN_TOOLBOX_2.0

- `README.md`, `Services/ToolLauncher.cs`, `Views/MainWindow.axaml.cs`: native tools and navigation.
- `Views/PixelArtConverterWindow.cs` and `Services/PixelArtService.cs`: selected-file live preview, zoom, palettes, 100ms-ish preview pump, editable numeric slider fields, master configurations.
- `Services/PixelPalettePresets.cs` / `Services/PixelArtConfigStore.cs`: persistent user presets.
- `Views/SpriteBatchWindow.cs` / `Services/SpriteBatchService.cs`: batch PNG naming, scaling, nearest-neighbour and safe export/handoff.
- `Notes/Plans/PIXEL_ART_CONVERTER_IMPLEMENTATION_PLAN.md`: independent image pipeline.

**Critical existing issue to respect:** Snow/wetness currently independently classify foliage using the current pixel colours (green or pink). Once a seasonal shader turns green leaves orange, these subsequent classifiers may STOP recognizing foliage. The combined shader **must calculate foliage influence from the original sampled pixel before season colour grading**, then pass/reuse that influence for seasonal colour, wetness and snow. Verify actual runtime dynamic shader-injection order; adding a naïve tint after the wrong anchor can break wind, wetness, snow or shadows.

## 3. Shared texture-mask specification — agree BEFORE implementation

The Studio and Godot must share one simple **versioned, testable asset contract**, rather than tying either one to the other's internal code.

- **Mask identity:** One optional `*_foliage_mask.png` **per final TreeVisualVariant texture**. No per-tree-instance mask node. Mask goes beside its species/variant artwork; never hardcode oak/pink file names into shared logic.
- **Mask format:** Standard PNG; **R = G = B = influence (0..255), alpha = 255** throughout, including empty source regions where RGB is zero. Godot reads **mask.r**; do not encode selection only in PNG alpha.
- **Meaning:** black = no seasonal change (bark/trunk/root); white = full foliage tint; intermediate gray = softened selection. Studio can maintain multiple editable masks, but exports ONE combined effective mask for runtime.
- **Geometry:** Mask image matches its source artwork **exactly** in width/height, origin, crop, transparent margins, flipping and UV mapping. Mask zero wherever source artwork alpha is zero. No automatic trim/centering.
- **Studio editing:** Foliage selection and **ProtectionMask** (trunk/branches) are independently paintable. EffectiveMask = FoliageMask × (1 − ProtectionMask); export this effective result, not two runtime textures.
- **Godot fallback:** With mask -> sample authored influence with the same **wind-shifted `sample_uv`** as sprite; it replaces the existing green/pink heuristic. Without mask -> retain existing green/pink heuristic. Do NOT multiply painted masks by green colour heuristic: that would reject orange/pink/blossom foliage.
- **Texture/filter QA:** Test import sampling, bit-depth, colour space, clamp/filter/mip settings at source scale, and mirroring. Prefer appropriate lossless mask import settings and no unintended colour/mask bleeding. Use the sprite's source alpha to gate changes. Do not tint silhouettes/shadows.
- **Identity check:** Studio records source dimensions and optional SHA256. If the source is replaced, resized, repixelated or recropped, flag old mask as potentially invalid.
- **Runtime data:** Add optional mask to `TreeVisualVariant` rather than one species-wide texture: species variants can have distinct leaves, shapes and UVs. Current `TreeDefinition.SnowFoliageProfile` remains default fallback when no authored mask exists.
- **No per-instance overhead:** Material/cache keys must account for base shader/material, foliage profile, mask identity and mirrored orientation; group by variant and share materials when possible. Preserve streamed-chunk register/unregister/restoration and wetness/wind subscriptions.

### Pipeline order matters: two separate user workflows

**A. Dynamic Godot seasons (RECOMMENDED):**

ChatGPT generated transparent PNG → **Pixel Art Converter** (chosen pixel style) → **Sprite Batch** (final physical dimensions and name) → **Seasonal Sprite Studio** (author/export mask for that EXACT FINAL PNG) → import original final PNG + mask in Godot → one texture dynamically tinted with season.

**B. Optional baked seasonal PNGs:**

ChatGPT transparent PNG → **Seasonal Sprite Studio** (mask selection and per-season colour/snow preview, optional four baked variants) → Pixel Art Converter (same saved master config for all variants) → Sprite Batch → Godot, using separate variants only if deliberately needed.

**Do not accidentally use a mask authored BEFORE pixelation/resizing on a DIFFERENT final texture.** Pixel Art Converter currently retains canvas size in some modes, but changes pixel boundaries; identical outer dimensions alone do not guarantee mask alignment. Future linked tools may resample masks with an identical geometry pipeline, but that is **not V1**.

## 4. Avalonia Studio feature scope

### Main workflow (one sprite first)

1. Open **Game Development → Seasonal Sprite Studio**, drop one transparent PNG; display dimensions and original visual on a checkerboard with wheel zoom, drag pan and Fit.
2. **Auto-detect foliage** from green colour/chroma + crown position; inspect *actual selected pixels* with an adjustable overlay. Include similar-colour picker to support pink blossoms, unusual leaf colours or dark leaves.
3. Paint masks with **Add Foliage**, **Remove Foliage**, **Protect Trunk**, **Unprotect** brushes; sliders + **click-to-type values with no giant arrows** for brush size/opacity, detector tolerance, hue etc. Interpolate rapid brush motion. Bounded undo/redo.
4. Toggle **Original / Foliage Mask / Protected Trunk / Effective Mask / Seasonal Preview** independently. Hide/show layer does not delete mask content; regenerated auto mask must NOT delete manual corrections.
5. Per-season **Spring, Summer, Autumn, Winter** local previews. Colour transforms must preserve shading/luminance and only affect the effective foliage selection. Summer closest to source, autumn warm colours, winter cool green with optional patchy snow on EXISTING foliage.
6. Studio's winter snow is an **offline approximation for preview and optional baked PNG**, NOT a duplicate of Godot's authoritative runtime snow shader. Warn about slight visual differences; the **mask** is the shared contract, not identical pixel-for-pixel GPU/CPU output.
7. **Save Mask Project / Load Project:** versioned JSON plus lossless auto/manual/protection masks, source path/hash/dimensions and season preview settings; detect mismatch, permit explicit relink and preserve original.
8. **Export Godot Foliage Mask** first-class action: gray RGB, alpha opaque, matching final artwork dimensions, PNG naming collision safety, no source overwrites. Display source/mask dimension verification prominently.
9. Optional **Export Selected Season / Export All Four Seasons** as baked PNGs, same original canvas/alpha/anchor and predictable suffixes. These are not required for Godot dynamic seasons.
10. Later integrate direct handoff to Pixel Art Converter for baked variants. Do not force massive cross-tool refactors into V1.

### UX layout (same design language as existing converter)

- **Left:** input file, mask layers/visibility, undo/redo and mask controls.
- **Centre:** large checkerboard with source/mask/season preview and independent zoom/pan.
- **Right:** FOLIAGE DETECTION, MASK EDITING, SEASONAL PREVIEW, SNOW PREVIEW, PROJECT and EXPORT groups. Compact visible numeric fields alongside sliders; avoid NumericUpDown arrows.
- **Bottom:** current warning, save/export progress and Cancel. Manual art confirmation important: bark/trunk cannot be accurately separated for every flattened generated PNG automatically.
- Future multi-file queue must track **different masks per individual PNG**. Never reuse coordinate mask blindly across species or resolutions.

### Technical and performance rules

- ImageSharp processing and mask classification reside in `Services/SeasonalMaskService.cs` or similar pure service, no Avalonia UI dependency; one editor window and simple models. Don't overload PixelArtService with unrelated seasonal logic.
- Foliage and bark masks are full-resolution byte grayscale; edit non-destructively. Recompute detector only when relevant tolerance inputs change; changing autumn strength should only rerender final colour.
- Avoid per-frame redraw and copying giant bitmaps after every mouse movement. Brush history should store bounded stroke/region deltas instead of a whole 4K image per pointer event.
- Live preview uses cancellation/generation IDs, ~100–150ms throttled single pipeline and selected image only. Do not read Avalonia controls within Task.Run.
- CPU preview may use bounded lower-res intermediate while dragging; final image/mask export is full-resolution and exact dimensions.
- Dispose ImageSharp images, Avalonia bitmaps, intermediate streams, cancellation resources; handle corrupted/too-large PNGs clearly.
- Source always read-only. Export uses deterministic naming, preflight existing-file collisions, no overwrite by default, cancellation and scoped cleanup.
- Local user named presets are separate from per-artwork saved mask projects. Do not save dangerous output/overwrite choices as artistic presets.

## 5. Godot shader implementation guidelines

### Early minimum prototype (G1)

- Add **manual** season selection / tint strength in existing F9 Environment debug, initially Spring, Summer, Autumn and a cool winter tint **without replacing the current snow system**.
- One shared season value/progress, no per-tree _Process. Transition can be scrubbed directly; implementing automatic calendar/year simulation is *not a prerequisite*.
- Composition order contract: sample original pixel once at **sample_uv** after wind, determine foliage influence from **original RGB** (or authored mask when G2); seasonal tint original pixel RGB; apply wetness and existing snow based on original foliage influence; preserve alpha and shadow-only output; maintain fire/shadow/wind compatibility.
- Existing VegWetness injects before VegSnow using shader-code string anchors and their own foliage helpers. Do NOT simply add another blind string replacement; design/test composition or shared include with original influence and stable injection order.
- Oak green foliage + pink-blossom profile both tested. Return tint strength to zero -> identical existing look.
- New/returning streamed chunks instantly receive season params, dormant chunks no per-frame updates; original shared material identities restored properly.

### Optional authored mask hookup (G2)

- `TreeVisualVariant` receives optional foliage mask texture; the runtime consumer reads it using same UV as source. Validate geometry at import/assignment where practical.
- Foliage influence used for seasonal tint, wet darkening and snow on recoloured leaves. If optional mask absent, current heuristic still works.
- Cache bounded material variants; handle source material + mirroring + mask identity + profile; no per-tree material instance/per-frame parameter writes.
- Test exact bark/roots protection, mirrored sprites, sprite scaling, wind movement, wetness and snow coverage simultaneously, shadow-only and fire erosion, chunk unload/reload.

### Long-term shared seasons (G3)

- Promote `ENVIRONMENT/Seasons/` placeholders into **one** data-driven season/calendar state with `SeasonDefinition` resources, biome overrides and controlled transitions.
- Update shared uniforms **only when seasonal values actually change**, not every frame on every tree. Existing rain/snow weather controllers remain separate.
- Seasonal leaf particles / ambient colours can eventually use the same per-species season profile but are not prerequisites.

## 6. Revised Codex execution plan — replaces V1 S0–S7

| ID | Repository | Deliverable | Status |
| --- | --- | --- | --- |
| **H0** | Both (spec/docs) | Approve mask PNG contract, final-asset pipeline and shader composition order | NOT STARTED |
| **G1** | Fantasy 2D | Minimal manual F9 spring/summer/autumn/winter tint proof using existing foliage heuristic, preserving snow/wetness/wind | NOT STARTED |
| **A0** | BEN TOOLBOX | Offline ImageSharp foliage detector + editable mask/protection models + mask-export tests | IMPLEMENTED — Windows visual testing pending |
| **A1** | BEN TOOLBOX | Native Avalonia mask editor, brush/zoom/selection/layer toggles, last-request-wins preview | IMPLEMENTED — Windows visual testing pending |
| **A2** | BEN TOOLBOX | Save/reopen per-artwork mask projects, source-hash/dimension validation, safe mask PNG export | IMPLEMENTED — Windows visual testing pending |
| **G2** | Fantasy 2D | Optional authored mask per TreeVisualVariant, shader sampling, correct shared material composition | NOT STARTED |
| **A3** | BEN TOOLBOX | Spring/summer/autumn and snowy-LEAFY-winter previews + named seasonal presets | IMPLEMENTED — Windows visual testing pending |
| **A4** | BEN TOOLBOX | Optional baked variants, safe export, later handoff to Pixel Art Converter | NOT STARTED |
| **G3** | Fantasy 2D | World seasons/calendar, transitions, biome response, shared debug controls | NOT STARTED |
| **V** | Both | Build/runtime tests, actual sprite visual checks, performance, documentation reconciliation | NOT STARTED |

**Recommended order:** H0 → G1 quick shader check → A0/A1/A2 mask authoring → G2 integration → A3/A4 enhancements → G3 automatic world seasons. You can reverse G1 and A0/A1 after H0 if you want to concentrate on Toolbox first; **the studio never needs to wait for G3**.

### Acceptance checklist

- [ ] **H0:** a sample final-sized oak and blossom source + mask with pixel-perfect matching dimensions; Godot/Studio agree on RGB gray opaque-alpha contract.
- [ ] **G1:** live foliage-only autumn tint on an oak and pink blossom in F9; bark/shadows unchanged; snow/wetness/wind and streaming still work; no per-tree _Process.
- [ ] **A0:** synthetic green-leaf and brown-trunk test PNG correctly masks leaves, protection subtracts, alpha-zero areas excluded, no source mutation; saved gray PNG has expected RGBA bytes.
- [ ] **A1:** users can fix one incorrect foliage patch and paint trunk protection while zooming; edits persist during detection-tolerance changes; preview remains responsive.
- [ ] **A2:** reopen project and compare masks byte-for-byte; missing/mismatched source gives clear warning; exported mask matches source dimensions/name safely.
- [ ] **G2:** authored mask changes only leaves under wind UV, mirroring and scaling; absence uses existing green/pink fallback; shared material count bounded; snow on *orange* leaves still works.
- [ ] **A3:** summer/green, autumn/warm, spring/light, winter/leafy snow preview with retained trunk and original alpha; never claim it matches GPU snow pixels precisely.
- [ ] **A4:** optional four baked PNGs with identical canvas/ground registration; no collision/overwrite; downstream pixelation/resizing accounted for.
- [ ] **G3:** smooth 0–100% seasonal progress, new streamed chunks correct on first appearance, no idle per-tree polling.
- [ ] **V:** run .NET/Godot builds, unit smoke tests, representative oak/blossom Windows screenshots, weather/snow/wind/light regressions, frame profiling and cleanup; update phase statuses **only after actual tests**.

## 7. Extra ideas deferred from V1

- Import externally authored foliage masks to edit in Studio.
- Autumn progress distributed among leaf clusters rather than full-canopy colour change at once.
- Snow/wet foliage masks share the same authored selection at runtime.
- Optional direct Pixel Art Converter ↔ Studio handoff **with same transform applied to mask**.
- Per-tree/species palette choices, biome tint overrides, blossom variants, foliage particle colours.
- Portable Studio projects that include original source and versioned mask metadata.
- Bare winter branches require separately authored artwork; no magic "recover hidden trunk" from a flattened PNG.

## 8. Progress log / Codex task prompts

- **2026-10-23:** Original Seasonal Sprite Studio V1 plan created as offline seasonal-sprite generator; no code changed.
- **2026-10-23:** **V2 hybrid plan revised** after inspecting live Godot snow/wetness/wind shader composition and BEN TOOLBOX modules. The mask editor is now a first-class asset-authoring tool for dynamic Godot shader seasons, while retaining optional local seasonal previews and baked exports. **No code or shaders changed; all phases pending.**

**Codex — first Godot job:** "Read Fantasy 2D root README, CodePreferences, Shared/Snow and Shared/Wetness shader/adapters, VegetationSway shader and TreeDefinition/TreeVisualVariant. Implement H0/G1 only: F9 manual seasonal foliage tint for oak/pink blossom, with unchanged trunk/shadows and working snow, wetness and wind. Update docs, test, one scoped commit. Do not implement the full calendar or edit BEN TOOLBOX."

**Codex — first Toolbox job:** "Read BEN TOOLBOX README and the V2 Seasonal Sprite Studio plan. After H0 contract, implement A0 only: offline foliage mask detection with manual protection data model, PNG output exactly matching source dimensions and RGB-gray/opaque alpha contract, tests. Do not build a large editing UI or baked variants yet. Update notes and push one scoped commit."

**2026-10-23 implementation update:** See standalone notes section 17 for A0–A3 implementation details, actual .NET/service/headless results, format/limits and Windows checks still pending. H0/G1/G2/G3 and A4/A5 are not claimed implemented.
