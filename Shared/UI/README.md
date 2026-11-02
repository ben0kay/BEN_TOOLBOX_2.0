# BTX UI Kit — usage

The palette, buttons and panels are **opt-in**, not global recolours.
The source of truth is `Shared/UI/Styles/BtxColors.axaml`, with semantic
styles in `Shared/UI/Styles/BtxControls.axaml`. `App.axaml` includes both.

## AXAML
```xml
<StackPanel Spacing="12">
  <TextBlock Classes="btx-heading" Text="MY TOOL"/>
  <Border Classes="btx-panel">
    <StackPanel Spacing="10">
      <TextBlock Classes="btx-section-heading" Text="Tasks"/>
      <TextBox Classes="btx-input" Watermark="Input..."/>
      <Button Classes="btx-button btx-primary" Content="RUN"/>
      <Button Classes="btx-button btx-secondary" Content="PREVIEW"/>
      <Button Classes="btx-button btx-danger" Content="CANCEL"/>
    </StackPanel>
  </Border>
</StackPanel>
```

## C#
```csharp
using BEN_TOOLBOX_2_0.Shared.UI;

var start = BtxUiKit.CreateButton("START", BtxUiKit.ButtonKind.Primary);
start.Click += (_, _) => StartWork();
var input = BtxUiKit.CreateField("TARGET", new TextBox());
var panel = BtxUiKit.CreatePanel(input);
var header = BtxUiKit.CreateHeader("FILES / TOOL", "MY TOOL", "Subtitle");
BtxUiKit.ApplyButtonStyle(existingButton, BtxUiKit.ButtonKind.Secondary);
```

## Rules

- Keep native Button click/command, focus, disabled and keyboard behaviour.
- Remove conflicting inline Background, Foreground or BorderBrush values in a tool
  **only when** you consciously migrate that tool.
- Use Primary sparingly, Secondary normally, Danger for destructive/cancel, Quiet
  for low-priority actions. No idle decorative animation loops.
- The Storage Analyzer SA0 toolbar and outer cards are a small first demonstration.
  Folder Backup T3 is still unstarted; do not touch Asset Usage Finder accidentally.
- Validate actual .NET 10 / Avalonia 12 build, Windows hover/focus and high DPI
  before marking the visual design accepted.

## Angled HUD frame (optional)

For a responsive cut-corner frame with luminous accent rails, use
`BtxTechFrame` instead of a normal `Border`. It uses a cached
`StreamGeometry` during drawing and has **no animation timer**, so it doesn't
redraw constantly while idle.

C# example:

```csharp
var frame = BtxUiKit.CreateTechPanel(toolContent, emphasized: true);
// Non-emphasized for ordinary sections:
var section = BtxUiKit.CreateTechPanel(sectionContent);
```

AXAML example:

```xml
<Window xmlns:btx="using:BEN_TOOLBOX_2_0.Shared.UI" ...>
  <btx:BtxTechFrame Classes="btx-tech-frame btx-tech-active">
    <TextBlock Text="SCAN CONTROLS"/>
  </btx:BtxTechFrame>
</Window>
```

The theme controls `BtxTechGlow` / `BtxTechGlowActive` and the shared
palette. Reserve `btx-tech-active` for a selected/important panel. Keep input
focus rings, button styles and real interactions separate from decorative frames.

**Storage Analyzer SA0** demonstrates the effect on the top scan toolbar
(emphasized) and all outer sections (normal). The blank-state content remains
unchanged. Animated moving light sweeps are intentionally deferred; they are
not necessary for the static futuristic framing and would need opt-in motion
and frame-rate/performance review.

## Folder Backup — first full tool migration (T3)

`Views/RemainingToolsWindow.cs` now uses `BuildBackupLayout()` exclusively
for `kind == "backup"`: central BTX header, chamfered setup/filter/results
panels, styled inputs and Browse buttons, BTX action buttons and a persistent
status/footer. The original `BuildLegacyLayout()` retains Artwork Prompter and
Asset Usage Finder styling/behavior.

The backup activity bar is **indeterminate**, only visible while Preview or
Create Backup is really running; it is not a fabricated percentage. Every
existing service call, source/destination input, exclusion default, first-400
preview limit, status message, and cancellation hook is retained. No new
automatic filesystem scanning or cleanup actions were introduced.

If modifying this layout, keep backup-only style classes within
`BuildBackupLayout` or the `kind == "backup"` picker branch, and verify the
other two modes before merging. Windows build/hover/DPI and smoke tests still
determine whether T3 is visually accepted.
