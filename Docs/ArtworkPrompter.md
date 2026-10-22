# Native Artwork Design Prompter: fantasy and sibling variants

Open **Game Development → Artwork Design Prompter**. The native tool opens its
own window, defaulting to **Fantasy survival / top-down**, **Tree**, an editable
oak-tree example, and a **Charred / burnt** sibling treatment. The prior
sci-fi/isometric direction remains selectable.

## Workflow

1. Edit the main asset subject, shape, material, details, colours and notes.
2. Enable **Generate matching sibling artwork**.
3. Choose a **Sibling treatment** (for an oak, try **Charred / burnt**).
4. Leave **Preserve identity, scale and core structure** enabled for continuity.
   Trees and shrubs also have **Keep main trunk and roots intact**.
5. Optionally add **Variant-specific notes**.
6. Both prompts update live; use **COPY MAIN**, **COPY SIBLING** or **COPY BOTH**.

The sibling prompt describes the original as an identity reference, then
prioritizes the selected transformation over the healthy material or foliage.
It requests a shared camera angle, asset scale and ground anchor for paired
sprites. In the charred oak preset, foliage disappears into thin, bare,
partly broken branches; major trunk/root structure remains and bark is
scorched. Active flames and smoke are excluded by default.

Additional presets cover dead, winter-leafless, autumn, snow, rain-wet,
diseased/corrupted, felled stump, young sapling, mossy, weathered and custom
sibling art where relevant to the asset category. Presets are offered only
for categories where they make sense.

## Data-driven maintenance

All presets are in Services/ArtworkSiblingPromptService.cs.

- Categories: available main asset types.
- Styles: project/art-direction templates.
- Variants: reusable transformation recipes, each with supported categories
  (the special "*" category matches everything).
- Examples: optional editable starting fields for each category.

Adding a treatment changes service data only; no new window click handlers
or species-specific generator branches are required. The tool generates
text prompts only. It does not change Godot resources, create sprites or
guarantee that two separately generated images register pixel-perfectly.
For better continuity, pass the main asset image as a visual reference when
you generate the sibling.

## Windows smoke test

From the toolbox repo root, run: dotnet restore, then dotnet run.

- Open the planner. Verify the oak main prompt and leafless charred sibling.
- Verify the sibling has bare branches and preserved roots with no active fire.
- Switch Tree to Rock: incompatible sibling treatments disappear. Use
  **LOAD CATEGORY EXAMPLE** to replace the edited fields with a rock example.
- Turn off the sibling option: the main remains and sibling-copy is disabled.
- Re-enable it, then select Snow-covered, Wet and Custom and check prompts.
- Edit notes, shadow/transparency options and art direction; check both outputs.
- Check that Asset Usage Finder and Folder Backup still launch as before.

The legacy PowerShell planner remains in BEN_TOOLBOX_OLDVERSION and is
unmodified. The old native quick prompter code remains as a compatibility
path inside RemainingToolsWindow, but Game Development opens the new dedicated
artwork planner. The future fantasy game Avalonia-to-Godot authoring/catalog
integration is a separate project and is not implemented here.
