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

## Automatic Fantasy 2D sprite references

**Automatically reference existing Fantasy 2D project artwork** is on by
default. The **Reference GitHub repository** field starts with
https://github.com/ben0kay/isometric-fantasy-2d but can be changed or the
whole reference feature turned off.

The generated main AND sibling prompts ask a browsing-capable AI to read
the project's current README and inspect existing PNG/WebP sprites of the
selected category BEFORE generating new artwork. For the default tree
category, verified production references are:

- WORLD/Content/Vegetation/Trees/Oak/Sprites/tree_01.png
- WORLD/Content/Vegetation/Trees/PinkBlossom/Sprites/tree_pink_blossom_01.png
- WORLD/Content/Vegetation/Trees/Oak/Sprites/oak_charred.png

These become clickable GitHub file links *inside the generated prompt*.
Grass references canonical Grass/Sprites PNGs; Rock uses its existing
WebP asset; other categories ask the AI to search the most relevant
repository subfolder and explicitly state when no examples exist.
When a different repository is entered, the program does NOT pretend
that the fantasy project's filenames also exist there; instead it
requests a fresh category-specific search.

**Important limitation:** A text prompt cannot itself fetch, inspect,
embed or attach the actual PNG image pixels. The AI/image tool needs
repository browsing access to use links as a visual reference.
If the destination tool cannot access GitHub, attach the relevant
existing images manually; the generated prompt instructs the AI not to
claim it viewed files that it could not open.

## Output image count and exact common base

For an enabled sibling, **COPY BOTH (2 IMAGES)** now begins with a
single explicit instruction to generate **exactly 2 separate images**:
image 1 is the main asset; image 2 is its transformed sibling. This
explicitly forbids merging both assets into one collage, side-by-side
composition or sprite sheet. Copying **MAIN** or **SIBLING** separately
still requests one image per prompt. Turning off the sibling means
only the main prompt is generated.

The **Match the exact ground/base silhouette in both images** checkbox
(default ON) adds a strict rule to preserve the same lower trunk, root
flare/ground footprint, base outline and anchor for both siblings.
It supplements the existing 'Keep main trunk and roots intact' rule.
Text-only prompts alone cannot guarantee pixel-perfect base registration;
for the closest match supply the actual main sprite as an image reference
when creating the burnt/charred version.

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
- Confirm the combined prompt starts with "exactly 2 SEPARATE images" and
  contains two individually identified image instructions; disable sibling
  to verify main-only output.
- With repository references ON, check Tree includes three verified sprite
  links and Rock lists only its relevant image. Turn references OFF to check
  that no repository link remains in either prompt. Change the URL to a
  different GitHub repo; old fantasy-specific sprite links must disappear.
- With ground/base matching ON, verify the sibling prompt explicitly says
  EXACT same ground-contact foundation and anchor; turn it OFF to remove
  that strict instruction.
- Check that Asset Usage Finder and Folder Backup still launch as before.

The legacy PowerShell planner remains in BEN_TOOLBOX_OLDVERSION and is
unmodified. The old native quick prompter code remains as a compatibility
path inside RemainingToolsWindow, but Game Development opens the new dedicated
artwork planner. The future fantasy game Avalonia-to-Godot authoring/catalog
integration is a separate project and is not implemented here.
