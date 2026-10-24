# BTX UI Kit — Shared Avalonia Visual Design System Implementation Plan

> **STATUS: PLANNED — NO UI CODE CHANGED BY THIS PLAN**
>
> **Created:** 2026-10-24
> **Repo:** ben0kay/BEN_TOOLBOX_2.0
> **Primary visual reference:** The current **YouTube Audio Downloader** window, as shown by the user's screenshot and implemented in `Views/YouTubeAudioDownloaderWindow.cs`.
> **First migration / proving ground:** **Folder Backup**, currently the `kind == "backup"` mode inside `Views/RemainingToolsWindow.cs`.
> **Scope:** Extract the downloader's dark navy / bright cyan visual language into opt-in reusable Avalonia styles, tokens and layout components. Apply the UI kit gradually. **Do not rewrite tool functionality.**

## 0. Read before coding

1. Read the project root `README.md`, this document, and [future feature folder cleanup](FEATURE_FOLDER_CLEANUP_TASK.md). The latter requests moving feature-owned code into feature folders **later**, while genuinely shared UI belongs under `Shared/`.
2. Read the *current versions* of:
   - `App.axaml` (currently only `<FluentTheme />` in the styles collection, with dark requested theme).
   - `Views/YouTubeAudioDownloaderWindow.cs` (**visual source of truth**).
   - `Views/RemainingToolsWindow.cs` (both Folder Backup and Asset Usage Finder share this window).
   - `Views/MainWindow.axaml` and `Views/MainWindow.Dashboard.cs` (shell/dashboard must not accidentally change).
   - Other native tool examples, and any UI infrastructure added since this plan.
3. Check `.csproj`, Avalonia version, existing resources, selectors and the current branch before writing code. Match **Avalonia 12.x actually in the repository**, not generic WPF syntax.
4. When conflicts exist, prefer the **real downloader's current appearance and behavior** over literal colours/examples in this plan, then document differences.
5. Implement **only the requested phase(s)**; build and run relevant tests, update phase statuses with actual evidence, and create one scoped commit per job. Do not move the whole repo or migrate every tool in a single pass.

## 1. Goal: one visual identity, independent tools

The user **prefers the Audio Downloader's look** (outlined glowing navy panels, vivid cyan headings, small uppercase section labels, soft muted descriptions, slender inputs, consistent buttons, neat status/footer) over the current much plainer Folder Backup window.

Produce a small, practical **BTX UI Kit** for *all future native Avalonia toolbox windows*. New tools should use it by default. Existing tools should **opt in individually** as they are migrated. The UI kit is **not** a single rigid page template: sprite editors, inspectors, progress-based utilities and maps have different content requirements. Share their visual language and reusable building blocks; permit layouts that fit each tool.

**Out of scope now:**
- Rebuilding BEN TOOLBOX's main dashboard or sidebar.
- Mass-overriding every `Button`, `TextBox` and `TextBlock` in `App.axaml`.
- Rewriting YouTube extraction/downloading, backup execution, file selection, or error handling.
- Introducing a third-party UI framework, large dependency, heavy animation/shader system or runtime designer.
- Implementing a theme picker; dark/cyan only initially, but keep resource design compatible with future themes.
- Performing the separate **feature-folder source cleanup** task.
- Replacing Windows title bars with frameless custom chrome; not necessary to match the tool's interior style.

## 2. Current implementation facts (2026-10-24 inspection)

### Downloader

`Views/YouTubeAudioDownloaderWindow.cs` currently:
- Sets `Background = #060F1C` on the Window.
- Defines private brushes `Cyan = #35D9FA` and `Muted = #8AAFC4`.
- Adds local style overrides for `TextBox` and `ComboBox`.
- Builds the title/header and editor/tool panels using grids, StackPanels, a reusable local `Panel(Control)` method (subtle gradient, thin cyan border and glow).
- Uses local `Field(label, control)` and `ActionButton(title, action, primary)` helpers, with colours and paddings set in C#.
- Uses a two-column main body with responsive stacking, a separate status/progress area and collapsible diagnostics log.
- Has real stateful controls, queue, async jobs, cancellation, progress/status and scrollable panel content. These must remain functional while moving appearance definitions.

### Folder Backup

`Views/RemainingToolsWindow.cs` currently:
- Uses background `#091522`, title `#86E5F6` and generally default Fluent buttons/inputs.
- Holds both **Folder Backup** and **Asset Usage Finder** behind a `kind` field. **Do not accidentally restyle Asset Usage while styling Folder Backup.**
- Backup path has source/destination inputs with Browse, excluded folder/extension fields, list preview, **CREATE BACKUP**, **PREVIEW**, **CANCEL**, and status message. The processing logic is already implemented and must be preserved.
- Uses one generic vertical StackPanel without downloader-style grouped frame/header/status.

### Theme entrypoint

`App.axaml` currently requests `Dark` theme and includes `<FluentTheme />` only. There is **no existing shared BTX theme** to extend yet.

## 3. Exact V1 design tokens (copied from actual downloader)

Start with the values below; centralize once and reference them. Do not independently re-enter hex literals in every new tool.

| Token / purpose | Current value | Notes |
| --- | --- | --- |
| `BtxWindowBackground` | `#060F1C` | Deep navy window background |
| `BtxPanelGradientStart` | `#10283B` | Angled panel gradient start |
| `BtxPanelGradientEnd` | `#071321` | Panel gradient end |
| `BtxPanelBorder` | `#16748E` | Thin panel outline |
| `BtxPanelGlow` | `#1628CDED` | Subtle border shadow/glow; use sparingly |
| `BtxAccent` | `#35D9FA` | Primary bright cyan |
| `BtxTextMuted` | `#8AAFC4` | Secondary text, hints, small section labels |
| `BtxInputBackground` | `#061420` | TextBox/ComboBox surface |
| `BtxInputBorder` | `#24627C` | Input border |
| `BtxButtonPrimaryBackground` | `#0C4058` | Primary actions |
| `BtxButtonPrimaryBorder` | `#2ED6FA` | Primary action outline |
| `BtxButtonSecondaryBackground` | `#10283A` | Secondary actions |
| `BtxButtonSecondaryBorder` | `#20506A` | Secondary outline |
| `BtxProgressTrack` | `#112A3C` | Progress-bar track |

Recommended foreground `#E8F4FF` (from current Folder Backup) on dark surfaces; **confirm actual downloader's default inherited text colour** and standardize explicitly to a readable value, rather than guessing about theme inheritance.

**Typical geometry/type:**
- Tool window/content outer margin: **20 px** (downloader) with 14–16 px section gap.
- Panel padding: **16 px**, border **1 px**, small corners **3 px**, restrained glow.
- Main heading: **28 px bold uppercase cyan**; accompanying micro-kicker ~11 px muted, description ~12 px muted.
- Section headers: **15 px bold cyan**, field labels **11 px bold muted**; 6 px field spacing.
- Body text normally around **12–14 px** (not all body text forced to 11 px).
- Buttons: ~12×9 px internal padding with defined primary / secondary / destructive / disabled / hover / focused treatments.
- Input fields have a consistent compact height/radius/border, actual selection/caret contrast and placeholder contrast.
- A11y: support 125–200% DPI, keyboard focus visibility, tooltips/descriptive labels and text wrapping. Do not sacrifice readability for a sci-fi appearance.

**Use semantically named resources**, not `#35D9FA` everywhere and not `Blue1`/`Color2`. Define brush resources for styles; use dynamic resources where future switching is justified. Avoid runtime theme rebuilds in V1.

## 4. Recommended shared source structure

The repository already has a backlog item to group feature code into `Features/{category}/{tool}` and keep genuinely shared code under `Shared/`. Align this task with that direction:

```text
Shared/
  UI/
    Styles/
      BtxColors.axaml              # Named palette/brush resources
      BtxTypography.axaml          # Titles, subtitles, labels, status, hints
      BtxControls.axaml            # Opt-in styles for common controls & states
    Controls/
      BtxToolHeader.cs             # Header with icon/title/kicker/subtitle
      BtxPanel.cs                  # Consistent outlined gradient panel
      BtxField.cs                  # Label + content + optional hint
      BtxStatusPanel.cs            # Optional shared status/progress presentation
    BtxUiKit.cs                    # Optional tiny helper for attaching semantic classes / creating layout
App.axaml                         # Include/register shared styles without unscoped recolouring
Notes/Plans/
  BTX_UI_KIT_IMPLEMENTATION_PLAN.md
```

These are **proposed** filenames. Codex may consolidate types/resources when it materially reduces complexity. Don't introduce a ViewModel, code-behind, separate designer and four files for a component that is a one-line class helper. Keep feature-specific windows/services in their **current** locations until a separate source cleanup is authorized. Do **not** migrate Folder Backup logic into `Shared/` just because its visual controls become shared.

## 5. Architectural choices and safeguards

### Opt-in, not global breakage

- Keep Avalonia's `FluentTheme` and existing application behavior.
- Register shared palette/style resources in an appropriate application dictionary, but scope **control styling to explicitly opted-in BTX tool windows/containers** or semantic classes (e.g. `btx-tool`, `btx-field`, `btx-primary`, `btx-secondary`, `btx-heading`, `btx-panel`).
- Prefer controls/styles with stable names and ordinary visual states rather than styling every child of every window via a global `TextBox` selector.
- Avalonia C# code-built controls can set `Classes` or use a small shared factory; new tools should not need to copy hardcoded brushes.
- **Important Avalonia precedence:** many existing controls set `Foreground`, `Background`, `BorderBrush`, `Padding`, etc. directly in C#. Local values can beat style setters. On each migration, explicitly remove the redundant local styling or route it through shared resources. Do not assume a style magically overrides existing local values.
- Protect the main dashboard, File toolbox windows that are not migrated, sprite editors and in-tool previews from accidental theme changes. A style cannot change app business logic.
- Use a few *well-named* styles or small compositional controls, not a multi-hundred-line generic layout DSL.

### Shared header/panel/field/status

- **BtxToolHeader:** single consistent icon+headline+kicker+subtitle; user-provided text and correct wrapping; no hardcoded "AUDIO DOWNLOADER" inside shared component.
- **BtxPanel:** thin cyan border, navy gradient, 16 px inner spacing, no excessive shadow. May be a helper/factory or an actual Avalonia control. Allow tools to supply arbitrary content.
- **BtxField:** muted uppercase section/field label with consistent spacing and optional explanatory text. Must work with TextBox, ComboBox, radio/check groups and complex picker rows.
- **BtxStatusPanel:** a common shell for status text, details, optional progress and controls; **do not force every tool to have a fake progress bar**.
- **Buttons:** semantic classes for primary, secondary, danger, compact, disabled; ensure hover, pressed, keyboard focus and busy/loading states are visually distinct.

### Responsive layout conventions

- Large screens: header across top, two-column content where useful, bottom activity/status and optional diagnostic expander.
- Narrow screens: stack columns *without* overlapping controls or horizontally clipping browse buttons; scroll the middle content.
- Status and action controls should remain visible while scrolling when reasonable; don't trap essential actions inside scrolling content.
- Don't require every tool to mimic downloader's two-column grid: a large editor canvas or file table may need a different arrangement.
- No idle redraw loop/animation just for glowing panels. Native controls, normal gradient brushes and cached resources should remain cheap.

## 6. Phased implementation and acceptance

| Phase | Task | Status |
| --- | --- | --- |
| **T0 — Design audit/contract** | Inventory downloader visual patterns and existing window types, confirm exact tokens, choose opt-in scoping and shared layout conventions, capture baseline screenshots | **NOT STARTED** |
| **T1 — Shared palette and styles** | Implement resource dictionary + semantic type/text/control classes with state styling, integrate opt-in theme safely alongside FluentTheme | **NOT STARTED** |
| **T2 — Reusable layout components** | Implement minimal header, panel, field and optional status components/helpers; provide a simple usable example/template for future Codex jobs | **NOT STARTED** |
| **T3 — Folder Backup pilot** | Redesign **only backup mode** to the downloader-inspired panels, controls and status while keeping source/destination, exclusions, preview, backup, cancel behavior | **NOT STARTED** |
| **T4 — Downloader refactor/validation** | Gradually point Audio Downloader to shared styles/components, remove duplicated local colour/style methods, ensure screenshot closely matches original and functionality untouched | **NOT STARTED** |
| **T5 — Adoption & staged rollout** | Document mandatory UI guidelines for future native tools, inventory other windows and migrate one approved window at a time; possible theme variants later | **NOT STARTED** |

**Recommended execution order:** **T0 → T1 → T2 → T3**. Show the user Folder Backup's Windows result before implementing T4/T5. Do not alter existing downloader styling until the shared system is proven in the pilot, except to inspect/reference its original values.

### T0 — acceptance
- [ ] Inspect source (not just screenshots) and record current palette + typography/button/input/panel structures.
- [ ] Inventory existing global styles and windows with local setters; classify those likely to be affected.
- [ ] Decide precise XAML include/resource + selector strategy suitable for installed Avalonia version, avoiding global side effects.
- [ ] Baseline screenshots from a **real Windows run** when available; if only headless/Linux accessible, mark Windows checks pending. No invented screenshot claims.
- [ ] Document which shared components help existing tool windows and which are optional.

### T1 — acceptance
- [ ] Named brush/color resources in one place; no duplicate unrelated palette constants.
- [ ] Opt-in styling applies to selected window controls only; the untouched app/dashboard retain their present look.
- [ ] Button default/hover/pressed/disabled/focus visibly legible; TextBox/ComboBox/CheckBox/RadioButton/Expander states readable as appropriate.
- [ ] Smoke-test resource loading in a headless Avalonia app or equivalent, including missing-key failures, duplicate keys, and style selector errors.
- [ ] No new dependency needed simply for colours or gradients.

### T2 — acceptance
- [ ] A demo/new-tool shell uses header, multiple bordered panels, labelled field with hint, action buttons and status (progress optional) **without copying hex codes**.
- [ ] Header layout wraps subtitle, panel accepts arbitrary content, field works with picker rows, keyboard navigation intact.
- [ ] Narrow resize stacks sections appropriately; no cut-off title/browse buttons; focus visible with keyboard.
- [ ] A short developer example shows how a future Codex adds a BTX-style window with **very little C#**.

### T3 — Folder Backup pilot, highest-priority visual milestone
- [ ] Screenshot resembles Audio Downloader's **interior styling**: #060F1C background, bright cyan main title, small kicker/description, bordered navy sections, consistent field labels and action buttons, explicit status/progress area.
- [ ] Existing Backup functionality is unchanged: choose source/destination folders; excludes; PREVIEW shows actual results; CREATE BACKUP still never overwrites/removes source; CANCEL functions correctly; status/errors understandable.
- [ ] Preserve backup's actual service calls and cancellation token lifecycle; no duplicate event subscriptions.
- [ ] Because `RemainingToolsWindow` also serves **Asset Usage Finder**, theme/layout changes must be gated to `kind == "backup"` or safely extracted into a backup-only view **without broader feature-folder moves**.
- [ ] Test backup with long paths, lots of preview results, narrow window and 125–200% DPI. List area scrolls, main actions remain accessible and UI stays responsive.
- [ ] Avoid infinite-height blank result panel: size dynamically and use an empty-state message, not a giant uninformative rectangle.
- [ ] User reviews actual Windows appearance before applying to more windows.

### T4 — preserve downloader design & functionality
- [ ] Migrate `YouTubeAudioDownloaderWindow.cs` references to the validated shared resources/components with **minimal UI layout difference**.
- [ ] Compare before/after Windows screenshots at same size/DPI, especially header, panel gradients, borders, foreground, button sizing, progress and log expander.
- [ ] Multi-link queue/category, download, cancel, folder selection, update/version buttons, diagnostics, error messages and window close remain intact.
- [ ] No changes to yt-dlp invocation/FFmpeg processing/update logic.
- [ ] Remove old hardcoded brushes/local selector definitions only when safe; avoid duplicate conflicting setters.

### T5 — style governance, one tool at a time
- [ ] Link this plan and short "BTX UI Kit usage" doc from root README; mark shared theme as **implemented** only after actual code exists.
- [ ] Every **new** native tool uses the shared resources/classes/components by default; exception for specialized windows documented.
- [ ] Existing tools remain individually opt-in: candidates include Folder Backup already done, Duplicate File Finder, Sprite Batch, Clipboard to PNG, Content Inspector, Internet Observatory, and Seasonal Sprite Studio.
- [ ] Prioritize **one tool per approved commit**, with functionality regression + Windows screenshot validation after each.
- [ ] Respect `FEATURE_FOLDER_CLEANUP_TASK.md` later; keep shared kit within `Shared/UI` even if feature files migrate elsewhere.
- [ ] The future Settings colour/theme selector is *not implemented now*; add only in a separate phase if requested.

## 7. Build, tests and regressions

Recommended run commands (adjust for actual projects):

```powershell
dotnet restore
dotnet build
```

- Run all relevant existing smoke/headless UI tests plus new **BTX UI Kit headless style-load smoke** when possible.
- **Windows visual verification matters**: the original issue is appearance. Test Folder Backup vs Audio Downloader side-by-side at standard and high DPI, wide and narrow sizes, normal/hover/focused/disabled state, when files/results are long and while operations are running.
- Confirm selectors/resource references load and no visual changes occur in unconverted windows; check basic keyboard focus/Tab navigation.
- **Do not claim "visual match" from compilation alone**. Screenshot/real run is needed to judge.
- Never delete user backup files or overwrite originals during testing. Use disposable test folders for Folder Backup smoke.
- No release of a style-only commit that silently breaks launch/backup/download flow. If tests cannot run, document the environment limitation and validation still required.

## 8. Risk register

| Risk | Prevention |
| --- | --- |
| Global TextBox/Button selector unexpectedly changes every tool | Use opt-in class/attached styles scoped to migrated windows only |
| Inline C# colour overrides shared styles | Explicitly remove/route old local setters during each tool migration |
| Folder Backup shares window with Asset Usage | Scope visual changes to backup kind; regression check the other mode |
| Reusable components turn into massive UI abstraction | Keep minimal helpers/styles and a small example; reuse actual Avalonia controls |
| Invasive source moves conflict with separate folder cleanup | Do not relocate tool logic or rename namespaces in this UI project |
| Small bright labels become unreadable at Windows DPI | Keep adequate contrast, sensible font sizes and wrapping; test keyboard/zoom |
| Fake progress or dynamic data | Status/panels reflect real tool state; no hardcoded demo progress in production |
| Repeated "glow" rendering hurts performance | Static brushes only, no 60-FPS timers for decorations |
| Main app/dashboard changes unintentionally | Preserve FluentTheme, opt-in migration and baseline screenshot checks |

## 9. Work log

**2026-10-24:** Created a documentation-only design-system plan after comparing user's screenshots and inspecting `App.axaml`, `Views/YouTubeAudioDownloaderWindow.cs`, `Views/RemainingToolsWindow.cs`, and the future feature-folder cleanup notes. Shared UI classes/styles and Folder Backup redesign **have not been implemented by this documentation task**. All T0–T5 pending.

**Recommended first Codex message:**

> Read the BEN TOOLBOX root README, `Notes/Plans/BTX_UI_KIT_IMPLEMENTATION_PLAN.md` and `Notes/Plans/FEATURE_FOLDER_CLEANUP_TASK.md`. Treat `Views/YouTubeAudioDownloaderWindow.cs` as the visual reference, preserve its current appearance and behavior, and implement **T0–T3 only**: audit the design, create reusable **opt-in** BTX palette/styles/components, then restyle only the **Folder Backup** mode of `Views/RemainingToolsWindow.cs`. Do not affect Asset Usage Finder or the main Dashboard; do not move feature files as part of this task. Run build/smoke tests and actual Windows UI checks if available, report limitations, update the phase table, and push one scoped commit.
