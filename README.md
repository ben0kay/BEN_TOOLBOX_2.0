# BEN TOOLBOX 2.0

A modular desktop toolbox built with **Avalonia UI**, **C#**, and **.NET 10**, developed in VS Code.

> Status: **Stage 3 — first native module under test**. This is not yet a replacement for the working PowerShell toolbox.

## Goals

- A resizable, polished sci-fi-inspired desktop dashboard.
- Separate pages for Game Development, File Toolbox, Inspectors, and Settings.
- Preserve and gradually integrate tools from the original `BEN_TOOLBOX_OLDVERSION/` directory without modifying them during UI development.
- Keep visual interfaces separate from the underlying tool execution logic, so the same approved tool operations can eventually be invoked locally or remotely.
- Support a future **Android companion** that can request work from the Windows PC and see task progress/results.

## Setup and run

Requirements: Windows, .NET 10 SDK, and optionally VS Code with Avalonia and C# extensions.

From the repository root:

```powershell
dotnet restore
dotnet run
```

The Game, File and Inspectors sections now launch allowlisted existing scripts from `BEN_TOOLBOX_OLDVERSION/` in separate Windows PowerShell windows. Run on Windows from the cloned repository, keeping that folder alongside the app.

## Project layout

- `App.axaml` — theme/application setup
- `Views/MainWindow.axaml` — current dashboard layout and styling
- `Views/MainWindow.axaml.cs` — Stage 1 navigation handlers
- `ViewModels/` — future presentation state and commands
- `Assets/` — icons and other bundled resources
- `Services/ToolLauncher.cs` — allowlisted local PowerShell launching (not a remote API)
- `BEN_TOOLBOX_OLDVERSION/` — unchanged legacy PowerShell tools
- `BEN_TOOLBOX_2_0.csproj` — project dependencies and framework target

## Roadmap

| Stage | Scope | Status |
| --- | --- | --- |
| 1 | Basic Avalonia dashboard, sidebar and navigation | Complete |
| 1.1 | Sci-fi dashboard visual polish and tighter layout | Implemented; needs Windows visual/build testing |
| 2 | Catalog ten existing tools and launch approved PowerShell utilities | Implemented; requires local Windows testing |
| 3 | Shared tool engine and more integrated tool views | Planned |
| 4 | Background jobs, progress, logs, history and cancellation | Planned |
| 5 | Secured local-network API on Windows | Future |
| 6 | Phone-friendly client (mobile web UI or Avalonia Android app) | Future |
| 7 | Optional access outside the home network through a private secure connection | Optional |

## Architecture direction for phone-to-PC control

The long-term design separates:

1. **Presentation:** desktop Avalonia views and eventually a phone interface.
2. **Tool catalog / engine:** named, strongly typed, allowlisted operations with explicit arguments, progress, output and cancellation.
3. **Windows execution adapters:** existing PowerShell scripts and new C# services.
4. **Future authenticated API:** submits approved tasks to the PC and returns status/results, without exposing arbitrary shell access.

The Android device will **request execution on the Windows PC**, rather than run Windows PowerShell locally. Remote access requires an explicitly configured service; using Avalonia alone does not provide it.

Security requirements for the future API: device authentication, encrypted connections, restricted access, permission-scoped operations, path validation, audit logs, and additional confirmation/authorization for destructive actions such as deleting files. Never expose raw PowerShell command execution over the network.

## Development rules

- Keep `BEN_TOOLBOX_OLDVERSION/` intact until an integration is deliberately tested.
- Prefer small, reviewable changes; preserve the project's ability to build and navigate.
- Do not couple tools directly to view click handlers once the Tool Engine work starts.
- Treat UI status badges as decorative until backed by actual telemetry.
- Avoid committing `bin/`, `obj/`, user-specific editor state or other generated artifacts.
- For now, test locally with `dotnet run` after each change.

## Stage 2 notes

- Tool lists are defined in `Services/ToolLauncher.cs`, not from arbitrary paths supplied by clients.
- PowerShell execution is **local only**, Windows-specific, and currently launches independent windows.
- Some older scripts may assume a particular folder or Godot project is present. Configure/select the correct project as required by each tool.
- The future mobile API must use a separate permission-checked job engine; it must **not** expose this launcher directly.
- Build outputs are ignored and removed from source control; a local `dotnet build` restores them.

## Stage 3: Native Duplicate File Finder

- The Duplicate File Finder launch button now opens an Avalonia-native window. All other utilities continue through the PowerShell launcher.
- Scanner logic is isolated in `Services/DuplicateScanService.cs` for eventual use by a job engine and remote clients.
- Source and destination each have independent recursive scan settings; same-name files can be SHA-256 verified.
- Result selection offers a preview for standard bitmap formats; CSV export is available.
- Recycle operations only allow verified matches, recheck before moving the source file to Windows Recycle Bin, and require confirmation. Destination files are never deleted.
- The original PowerShell version remains unchanged as a fallback. **Test on disposable sample folders before any deletion involving personal photos.**
- The native UI and filesystem operations require local Windows testing; this pass has not been compiled in the coding environment.

## Stage 3.2 — native Sprite Splitter (testing)

- Sprite Splitter launches a native Avalonia window; the original PowerShell tool is preserved.
- Uses ImageSharp for PNG alpha-gap analysis, bounding-box extraction and PNG export, isolated in `Services/SpriteSplitService.cs`.
- Supports suggesting cuts, manual click/drag of vertical cuts, removal, alpha threshold, gap, padding, individual/equal/square canvas exports and filename prefix.
- Existing output files are not overwritten; an incomplete export attempts to remove its own outputs.
- This initial pass uses a textual sprite region preview rather than thumbnail gallery. Test on sample PNGs before batch work.
- Stage 3.3 will convert Sprite Batch Tool separately.

## Stage 3.3 — Native read-only inspectors (first pass)

- File Size Inspector: ranks up to 200 largest files under a selected directory, skipping common generated folders.
- Resource Checker: checks static `res://` references in several Godot text formats against a selected project; does not replace comprehensive Godot validation or resolve every dynamic reference.
- Both use cancellable background scanning and separate read-only services in `Services/InspectorServices.cs`.
- Existing PowerShell versions remain unchanged and may have additional features; this is initial core functionality, not full feature parity.

## Stage 3.4 — Native world inspectors (first pass)

- **Lighting & Shadow Inspector:** exported C# defaults, shader uniforms, and selected scene/resource lighting or shadow settings, with filtering and file navigation.
- **Biome Population Inspector:** registered surface biomes from `BiomeCatalog.tres`, saved vegetation/enemy weights, relative selection shares, basic artwork checks, target frequency and locally unregistered definitions.
- Both are read-only, use background workers, and require selecting an external Godot project. Neither runs Godot nor claims live runtime values.
- Services: `LightingInspectionService.cs` and `BiomeInspectionService.cs`; presentation: `WorldInspectorWindow.cs`.
- Initial native parity only: compare against the preserved PowerShell versions on a real project before relying on reports.

## Stage 3.5 — Native Sprite Batch Tool (first pass)

- Native Avalonia batch PNG workflow: preview, 0–1 scale, numeric/letter suffix naming, optional species folders, and optional original copies in destination/RAW.
- Existing files are never overwritten; sources are not modified. Background execution, cancellation, rollback attempts and file-conflict checks are included.
- The original PowerShell version is preserved. **Godot Plant/Tree .tres generation and its parameters are not yet ported.** Use legacy tool when those features are needed.
- Open Game Development → Sprite Batch Tool. Choose existing separate source and destination directories, preview, then export.

## Stage 3.6 — Remaining native toolbox modules (first pass)

- Artwork Design Prompter: dedicated Avalonia window with **fantasy top-down** or sci-fi/isometric direction, category examples, editable primary prompt, category-appropriate **sibling variants** (charred tree, leafless, autumn, snow, rain-wet, etc.), structure/root continuity controls, live main/sibling preview and independent/combined clipboard copy. Variant recipes are data-driven in Services/ArtworkSiblingPromptService.cs; see [usage and test guide](Docs/ArtworkPrompter.md). Legacy randomization/locking, saved preset customization and file export are **not yet ported**. Default-on repository artwork references link the Fantasy 2D README and verified category PNG/WebP sprites (or search relevant folders for other categories), with an opt-out and editable repository URL. **COPY BOTH (2 IMAGES)** explicitly requests two separate images, and strict same-ground-footprint matching is optional. Reference links require browsing or user-provided image attachments; the tool does not inspect image pixels or make any Godot edits.
- Asset Usage Finder: native read-only project scan for direct and transitive `res://` references, resource UIDs and PNG ImageFolder references, with cancellation and results copy. Some dynamic usage and full legacy reporting are not included.
- Folder Backup: native preview and create-new dated backup folders with folder/extension exclusions, background execution, cancellation, and a summary/settings report. Sources are preserved. **Incomplete backups are retained and clearly marked** rather than silently removed. Legacy CSV/log/size/date filters and full reporting need parity work.
- Original PowerShell versions are unchanged. Test on disposable data first; compile and compare behavior with legacy tools before regular use.


## Items & Resource Inspector

Project Inspectors now includes a native read-only content explorer for the
isometric-new-2d schema-v1 export: items, harvest/ore/ground drops, crafting
recipes, biome bindings, shared profiles and validation diagnostics. It starts
with a labelled demo and can load/refresh ContentIndex.json from Content Pass 6.
Choose a game project to locate exact resources. Last project/report paths are
remembered separately; game data is never written.
See [the inspector guide](Docs/ContentInspector.md) for export setup, schema
limits, future editing plans and local verification.

## Native Clipboard to PNG (Windows)

1. Open **File Toolbox → Clipboard to PNG** and set an existing destination folder. This is remembered under LocalAppData.
2. Snip with `Win+Shift+S`; press `Ctrl+Shift+V` globally to quick-save to `Screenshot_YYYYMMDD_0001.png`, or use Preview Clipboard and Save PNG in the tool.
3. The app must be running to register the shortcut. It can be minimized; **closing it exits and unregisters the shortcut**. No tray/background launch on startup yet.
4. The shortcut may fail to register if another application already uses it; check the dashboard status. PNG and DIB clipboard formats are supported. The source clipboard is not modified and existing PNGs are never overwritten.

### Sprite Batch: drop files or paste folder

- Drag individual PNG images (including from multiple source directories) onto **DROP PNG FILES HERE**, or paste a source folder path / click **BROWSE** as before.
- The most recently chosen input mode wins. Typing a folder path switches back to folder mode; dropping PNGs switches to selected-files mode.
- Existing output folder field and Browse button remain unchanged. Preview and export use the same naming, scaling and create-new safeguards.
- Dropped paths are deduplicated. RAW archival prefixes source filenames with a unique sequence number to avoid collisions for files from multiple folders.
