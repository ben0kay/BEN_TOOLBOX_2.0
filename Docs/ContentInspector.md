# Items & Resource Inspector

Open **Project Inspectors → Items & Resource Inspector** from the toolbox.
This is a native Avalonia browser, designed as the inspection foundation for
future content editing, recipe authoring and item creation.

The first launch shows a clearly labelled demo subset. It is illustrative,
not your project's current content. File-location actions are disabled for
the demo. Load the real report to inspect actual data.

## Connect to isometric-new-2d

1. Apply Content Pass 6 in the game repository and rebuild in Godot.
2. Open `ITEMS/Discovery/ContentReportExport.tscn` and press F6.
   Godot prints the report directory; it contains `ContentIndex.json` and `.md`.
3. Open this toolbox inspector and choose **PROJECT**, selecting the game root
   containing `project.godot`. Choose **FIND REPORT**, or **LOAD JSON** to select
   the exported JSON directly.
4. Search/filter entries and follow their linked source, item, recipe or profile
   cards. **LOCATE FILE** selects the containing file in Explorer, retaining the
   full subresource locator in the status line.

FIND REPORT checks the standard PowerShell exporter folder under
`%LOCALAPPDATA%\BenTools\ContentReports\isometric-new-2d` and the standard
Godot user-data folder for the selected project's `config/name`. If your game
uses custom user-data settings or you exported elsewhere, use LOAD JSON.
It picks the newest matching file, but cannot verify that report belongs to
the selected project. Confirm the paths and report counts. Project selection
is for locating files, not live scanning or automatic game execution.

**REFRESH REPORT reloads the JSON snapshot.** Export again in Godot after
editing game content elsewhere. The report file's update time is shown on
load; it is not proof the resources have not changed since export.

## Views

- Items: stable identity, name, subtype, stack/weight/volume, capabilities,
  catalog registration, possible drop sources and consuming/producing recipes.
- Sources: plants, trees, rocks, ore and ground extraction rules, work/strength,
  reward counts/chances, biome selection bindings and surface ore settings.
- Recipes: ingredient/output links, counts, duration, category and registration.
- Profiles: exact profile/entry locations, drops, source references, shared/unused
  filters. Shared profiles explicitly identify all affected source definitions.
- Diagnostics: errors and warnings from Godot validation, exact paths/fields,
  related diagnostics beside individual definitions.

Search matches the entry name/ID, subtitle and path. Filters distinguish
registered/unregistered items and recipes, source kinds, shared/unused profiles,
and diagnostic severity. Resource links navigate between tabs.

Biome bindings describe configured selection, not actual active-campaign
assignment or guaranteed spawning. Ore settings are defined options; placement
rejection and older campaigns can prevent ores spawning. Drop chances apply to
successful extraction, not spawn chance. Ore primary quantities use remaining
units, bonuses roll per successful batch, and ground digs pay one unit.
The full context comes from the game exporter; the toolbox does not reimplement
Godot's resource validation or invent unresolved values.

The schema-v1 report does not currently export icon asset paths. This version
uses text cards; imported icon previews can be added when that metadata is
available. Demo content is intentionally partial. The actual report still
contains all supported exported content, including unregistered definitions.

## Read-only contract and persistence

This tool opens JSON read-only and only launches Explorer to locate files.
It does not write game resources, reports, catalogs, recipes, saves or game
settings. It does not run PowerShell or Godot automatically. Last project/report
paths are saved separately in
`%LOCALAPPDATA%\BEN_TOOLBOX_2_0\ContentInspector.json`.

Reports must have schema version 1 and required arrays. Files over 32 MiB,
unsupported versions, missing arrays and malformed nested rows are rejected.
An invalid game report is still inspectable, with a prominent validation-error
banner. If a new report cannot load, the previous view remains available;
the status explicitly says loading failed. Diagnostics are supplied by Godot.

## Future editing path

The report DTOs, reader and Avalonia window are separate. A later editing
service can resolve the exact containing file/subresource, reread current file
hashes, propose changes, show diffs, invoke Godot validation and apply reviewed
resource writes. Recipe creation and item creation must also update explicit
catalog links, preserve stable IDs and handle shared-profile/save implications.
An exported report is a discovery snapshot, not a writable second database.
No editing buttons are enabled or implied in this first pass.

## Local checks

Build/run the toolbox with `dotnet run`. Open the inspector in Project
Inspectors and verify demo tabs, search/filter and linked navigation.

Load an actual Pass 6 report. Check Bark → source trees → profile entries,
Bark → PlantFiberFromBark recipe → Plant Fiber, Iron → bonus and surface
settings, Sand/Clay one-unit yields, and Wilds biome bindings. Select the game
root and verify LOCATE FILE on a nested profile entry selects the right `.tres`.
Close/reopen and confirm last report/project paths are retained. Regenerate in
Godot and refresh; check the updated snapshot loads. Choose a non-report JSON
and confirm the old view is retained with a clear error. Use an error report
to check the Diagnostics tab/banner; use a report with shared profiles to check
shared-source messaging. Existing toolbox modules should still launch normally.

Preparation used source/schema/static checks only. This environment has no
.NET/Avalonia runtime; compilation and desktop UI checks remain local.
