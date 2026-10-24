# Future cleanup: organise source files by toolbox feature

Status: BACKLOG — documentation only; no files moved.
Requested: 2026-10-24.

## Goal

Make the repository match the application's navigation so Ben can find all code belonging to a tool in one place. Currently Views, Services and Models scatter each tool across technical folders.

## Proposed structure

- Features/Dashboard/
- Features/GameDev/PixelArtConverter/
- Features/GameDev/SpriteBatch/
- Features/GameDev/SeasonalSpriteStudio/
- Features/GameDev/SpriteSplitter/
- Features/GameDev/ArtworkPrompter/
- Features/FileToolbox/YouTubeAudioDownloader/
- Features/FileToolbox/FolderBackup/
- Features/FileToolbox/DuplicateFinder/
- Features/FileToolbox/ClipboardToPng/
- Features/Inspectors/<InspectorName>/
- Features/Settings/
- Shared/Controls/
- Shared/Services/
- Shared/Models/

Each feature folder should contain its own windows, processing services, models and feature-only controls. Keep genuinely shared code in Shared; do not duplicate dependencies just to put every file inside a tool. Keep Assets, Docs/Notes and Tests in clear dedicated locations. Keep app startup/project files at the root; put shared main-window navigation in an explicitly named shell location if needed.

The examples are a starting point, not an audited ownership map. Read the current README, tool catalog, routing and dependencies before assigning every file. Follow current UI category membership where it differs from these examples.

## Scope and approach

Moderate structural refactor, not a rewrite or feature change. Implement only when separately requested.

1. Inventory source files and identify feature ownership and shared dependencies.
2. Move one category at a time, preserving behaviour.
3. Initially preserve existing namespaces where practical. C# namespaces need not match directory paths; avoid mixing broad namespace renames with the first move unless needed.
4. Fix all affected explicit references, build and run relevant existing checks after each category.
5. Update README's repository map and active documentation links. Add a short guide explaining where to find each tool.
6. Commit coherent stages and record remaining work honestly.

## References to check when moving files

- Test projects with explicit linked-source Compile Include paths.
- Project-file includes/exclusions and any build scripts.
- XAML files, x:Class, resource URIs and asset paths; verify precompiled XAML still loads.
- Tool routing, shared controls and namespaces/imports if renamed.
- Documentation links, current implementation-plan paths and hardcoded filesystem references.
- Keep legacy PowerShell launch paths working; do not relocate BEN_TOOLBOX_OLDVERSION as part of this cleanup without auditing its scripts.

Most ordinary .cs files are discovered automatically by the SDK, so many moves need no project-file edits. This does not make the entire task a blind directory move.

bin and obj are generated build output, not feature code. Leave them ignored; do not move or commit their contents. Do not change persisted user-setting locations, exported formats or runtime output directories merely because source folders change.

## Acceptance

- [ ] Source folders visibly reflect Dashboard, GameDev, FileToolbox, Inspectors and Settings.
- [ ] A tool's feature-only files can be found together.
- [ ] Shared dependencies have clear ownership with no copied implementations.
- [ ] Main application builds; affected linked-source tests and existing headless checks pass.
- [ ] Native navigation/windows, XAML loading and legacy launch paths remain functional.
- [ ] README and active references describe the final layout.
- [ ] Actual Windows validation is distinguished from build/headless checks.

No implementation has started.
