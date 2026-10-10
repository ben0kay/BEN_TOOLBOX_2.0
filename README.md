# BEN TOOLBOX 2.0

A modular desktop toolbox built with **Avalonia UI**, **C#**, and **.NET 10**, developed in VS Code.

> Status: **Stage 2 — local tool launcher**. This is not yet a replacement for the working PowerShell toolbox.

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
