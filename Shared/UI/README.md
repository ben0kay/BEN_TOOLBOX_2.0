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
