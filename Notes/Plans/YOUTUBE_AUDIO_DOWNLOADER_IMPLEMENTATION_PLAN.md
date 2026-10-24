# YouTube Audio Downloader — Native Avalonia Implementation Plan

> **STATUS: Y0–Y3 IMPLEMENTED; Linux build/fake-process/headless checks passed. Windows end-to-end validation pending.**
>
> **Created:** 2026-10-24
> **Repository:** ben0kay/BEN_TOOLBOX_2.0 / main
> **Owner:** BEN TOOLBOX native C#/.NET 10/Avalonia tool
> **Planned navigation:** File Toolbox → **YouTube Audio Downloader**
> **Critical user request:** A prominent **CHECK FOR UPDATES** button to run the *correct* yt-dlp update method on demand when downloads begin failing. User currently runs yt-dlp manually in CMD.
>
> **Codex:** Follow the user's current explicit phase request. Do not implement all phases unsolicited. Read root README, current tool routing and relevant process/file utilities before touching code; update phase status/test evidence honestly and make one scoped commit per assigned job.

## 1. User goal and V1 boundary

Build a standalone native Avalonia audio downloader that pastes one YouTube video URL and saves audio as an MP3 using the **official external yt-dlp executable and FFmpeg**, with progress, cancellation, diagnostic messages and a manual yt-dlp update button.

**Do not reimplement YouTube's site/extractor APIs in C#**. Avalonia owns UI, C# launches/monitors the established yt-dlp process, yt-dlp downloads the audio stream, and FFmpeg converts when MP3 is requested. Typical audio-only input may be WebM/Opus or M4A; **do not download a 360p video as an intentional first step**.

V1:
- Accept **one video URL**, no playlist expansion by default.
- Format **MP3** with quality selector (default best available extraction / high-quality conversion).
- Output folder picker, remembered in user-local settings.
- **Download**, **Cancel**, **Open Output Folder**.
- Live title/status/progress/log area; friendly errors.
- Dependency diagnostics: exact yt-dlp path/version, FFmpeg/ffprobe path and availability, JavaScript runtime availability where possible.
- **CHECK FOR UPDATES** and outcome/version display.
- No need for Python, browser automation, external paid APIs or elevated privileges when a working standalone binary is available.
- Only download media the user is permitted to download. Do not implement bypasses for protected, DRM-controlled, paywalled or unauthorized content.

Deferred: playlists/multiple URLs, queue/history, metadata tagging/cover art, M4A/Opus/video format selection, subscription automation, native auto-installers, in-app binary distribution, login/cookies, custom command box/advanced arbitrary flags.

## 2. Confirmed official yt-dlp behavior

Read official upstream sources when implementing, because these tools evolve:

- yt-dlp README: https://github.com/yt-dlp/yt-dlp/blob/master/README.md (Update, Dependencies, Audio Extraction, progress/output options)
- yt-dlp EJS guide: https://github.com/yt-dlp/yt-dlp/wiki/EJS
- yt-dlp releases: https://github.com/yt-dlp/yt-dlp/releases

**Update flag is uppercase**: \`yt-dlp -U\` or \`yt-dlp --update\`, for **official release executables/binaries**. It is not the same as lowercase \`-u\` (username option).

**Installation-method difference:**
- Official standalone \`yt-dlp.exe\`: **manual update button can run that exact executable with \`-U\`**. Log current version, update output, exit code and new version. Binary updates use the current release channel.
- Python pip-managed installation: \`-U\` is **not the correct pip upgrade method**. The upstream recommendation is to rerun its installation/update command, e.g. \`python -m pip install -U "yt-dlp[default]"\` for the applicable Python environment; pipx/winget/choco similarly require their own manager-specific methods. In V1 **show a clear instruction and do not guess/run an installer, Python environment or elevated package manager silently**.
- If origin isn't confidently known, label **"Unknown installation type"** and show the resolved path + help instead of pretending update succeeded.
- Do not switch from stable to nightly/master automatically. Any future channel selector must be explicit, with stable the default.

**Runtime warning from user's screenshot:** "No supported JavaScript runtime could be found". Current yt-dlp YouTube extraction may need a supported JS runtime plus EJS solver components; Deno is recommended and enabled by default (versions/requirements subject to upstream change). Updating yt-dlp is **not** a substitute for a missing Deno/solver installation. Expose this as a separate diagnostic, with useful installation guidance and the upstream EJS page. Official release binaries and pip distributions may obtain solver components differently. Never silently download/execute a runtime from an arbitrary location.

**Conversion dependency:** FFmpeg/ffprobe is needed for MP3 extraction/conversion. Tell user if missing **before** starting rather than downloading then failing.

## 3. UX layout (follow BEN TOOLBOX look)

Register in the existing **Files** category (the UI labels it File Toolbox); no new top-level navigation section. A simple native window in the dark navy/cyan BEN TOOLBOX style is sufficient.

Suggested responsive vertical flow:

**HEADER:** YOUTUBE AUDIO DOWNLOADER / LOCAL · concise status and version (e.g. yt-dlp 2026.xx.xx / FFmpeg detected).

**DOWNLOAD**
- Paste a single URL text field, clear/select control; clipboard paste through native UI.
- Destination folder text + Browse; display chosen full path.
- Format: MP3 (V1 only). Quality: **Best/High/192 kbps/128 kbps** if supported; clarify that transcoding to a higher MP3 bitrate does not restore lost quality.
- **DOWNLOAD MP3** and **CANCEL** controls.
- Progress bar during download only, indeterminate during extract/post-processing; labels for **Resolving / Downloading / Converting / Complete / Failed / Cancelled**.
- Display retrieved video title if available; sanitized target filename/path; **OPEN FOLDER** after success.

**TOOLS / TROUBLESHOOTING**
- yt-dlp executable: detected absolute path and Browse/Select button; optional PATH auto-detect.
- \`yt-dlp --version\` and FFmpeg status; show which executable is actually being launched so user's CMD and app aren't accidentally updating different copies.
- **CHECK FOR UPDATES** (manual; never background auto-update on startup).
- Brief update log: existing version -> output -> new version or error. Explain pip-managed/path mismatch.
- Dependency check / troubleshooting text, including missing FFmpeg, missing Deno/EJS, permission failure, YouTube extractor changed, network issues.
- Collapsible bounded diagnostic output so the simple UI stays uncluttered.

Disable update **while any download or conversion is running** and disable download **while update is running**. Show a clear "Update finished; try your URL again" message **only if update really succeeded**. Don't imply an update fixes every YouTube download error.

## 4. Binary discovery, ownership and persistence

### V1 recommendation: use the user's existing working installation

The user already runs \`yt-dlp\` from CMD; avoid forced installation or bundling just to make V1 work.

1. Resolve \`yt-dlp.exe\` in PATH, or let user Browse to an executable. Persist the **exact absolute path** under user-local config; display it.
2. **Use the same chosen executable for both downloading and updating.** Critical: never run \`yt-dlp\` via one PATH and update a different binary via another PATH.
3. Discover FFmpeg/ffprobe via configured path, yt-dlp-adjacent binaries, or PATH; allow user to select folder if missing. Verify executables/versions via limited background diagnostic invocations.
4. Detect whether chosen yt-dlp is an official standalone release binary (when reliable). If unable to distinguish from wrapper/pip shims, do NOT blindly self-update; explain method choices. An initial explicit "Use updater (-U) for standalone executable" action is acceptable if user confirms the installation is a release binary.
5. If executable is in a protected location, report access failure, **do not request admin elevation automatically**. Offer clear steps for manual update or configure a user-owned official binary.
6. Per-user settings may contain only safe preferences: paths, output format/quality, last folder. Do not persist credentials or use shell history.

Potential future V2: optional explicit **Install/Manage Official yt-dlp** to user-owned LocalAppData with published release checksum/signature validation and rollback, but **do not build this into the initial update button without a separate reviewed plan**. Binary provenance matters.

## 5. C# process execution contract

Use a service (not UI event handlers) to encapsulate process invocation. Suggested \`Services/YtDlpService.cs\` or \`Services/YouTubeAudioService.cs\` and compact settings model/store; keep code understandable.

**No PowerShell or CMD shell invocation needed.** Use:
- \`ProcessStartInfo.FileName = exactValidatedExecutablePath\`
- \`UseShellExecute = false\`, \`CreateNoWindow = true\`
- \`RedirectStandardOutput = true\`, \`RedirectStandardError = true\`
- \`ArgumentList.Add(...)\` separately for each static flag/value and the user URL.
- \`Process.Start\` + asynchronous draining of both stdout/stderr and \`WaitForExitAsync(token)\` for cancellation/cleanup.
- Explicit valid output folder/path; no quoting or string concatenation commands that allow argument/shell injection.
- Strongly validate URL to supported YouTube video hosts (youtube.com/youtu.be/m.youtube.com/music.youtube.com as appropriate) and accepted scheme https. Default \`--no-playlist\` even if URL contains \`&list=\`; handle shorts/live/videos with clear errors when unsupported. **No arbitrary executable arguments supplied by user.**
- Avoid storing raw diagnostic credentials/tokens; redact sensitive-looking URL query parameters in logs if any. Don't print cookies.

**Suggested yt-dlp options to verify on pinned tested version (do not blindly copy):**

\`\`\`text
--no-playlist
-f bestaudio/best
-x
--audio-format mp3
--audio-quality 0
--newline
--paths <chosen-folder>
--windows-filenames
<YouTube URL>
\`\`\`

Note: \`--audio-quality 0\` is an FFmpeg conversion quality setting, **not the bit rate of the downloaded source**. For explicit 192k/128k options validate upstream syntax and reflect the distinction. Use yt-dlp's safe default output template or a carefully designed title + ID template to avoid same-title collisions. Let yt-dlp handle postprocessing/intermediate removal on success; don't recursively clean up user folders or delete unrelated \`.webm\` files.

**Progress:** Parse stable line-delimited yt-dlp output by using supported \`--newline\`, and consider \`--progress-template\` for predictable output where available. Download percentage may be unknown; display indeterminate instead of inventing. Post-processing state is separate; overall "100%" only when output exists and process succeeded. On completion, identify actual final filename/path via supported yt-dlp print-after-move option or explicit output validation rather than guessed title.

**Cancellation:** Cancel the active operation token, terminate the owned child **process tree** if needed (after bounded graceful handling), wait for exit/cleanup; avoid deleting existing user files. If partial downloads remain, tell the user and expose a deliberate later cleanup action; don't falsely report success. Close-window cleanup should also cancel active processes safely.

**UI thread:** All process, IO, diagnostic and updating runs off the Avalonia UI thread; marshal only bounded progress changes back. Bound log length. Prevent multiple simultaneous downloads/updates in V1.

## 6. Manual Update button — REQUIRED for initial release

User interaction:

1. Press **CHECK FOR UPDATES**.
2. If downloading/converting, disable button with "Complete or cancel current download first."
3. Resolve/show **the exact yt-dlp path** being used; if missing, show "Select yt-dlp.exe first" and installation link rather than pretend update attempted.
4. Detect/confirm origin. For official standalone \`yt-dlp.exe\`, asynchronously execute **that very executable** with \`-U\`; no shell, no arbitrary command.
5. Show one continuous status: "Checking ..." then verbatim bounded relevant tool output, error, existing/current version. Do not claim up-to-date until successful exit/output verified.
6. Refresh \`--version\` after updater exits. Treat updater's "already up to date" as successful no-change when exit status agrees.
7. If updater fails because executable is locked/unwritable, report permission/conflict and provide a clear manual path; **do not automatically elevate to administrator**.
8. If installed via pip/pipx/winget/choco or origin unknown, show its correct documented update approach or official guidance. Do **not** execute global package manager commands automatically.
9. If update completes but download still fails, diagnostics should distinguish missing FFmpeg, missing Deno/EJS, network or upstream extraction error. Offer direct link to official yt-dlp EJS instructions.

**Example compact UI**

\`\`\`text
YOUTUBE AUDIO DOWNLOADER
URL      [ https://www.youtube.com/watch?v=...      ]
Save to  [ C:\Users\...\Music                ] [ BROWSE ]
Format   [ MP3 ]         Quality [ Best ]
[ DOWNLOAD MP3 ] [ CANCEL ]

PROGRESS: 82%  50 MiB / 61 MiB
Converting with FFmpeg... (indeterminate)

TOOLS & TROUBLESHOOTING
yt-dlp  [ C:\tools\yt-dlp.exe ]  Version: 2026.xx.xx
FFmpeg  [ Detected ]   JS runtime [ Missing Deno ]
[ CHECK FOR UPDATES ] [ CHECK DEPENDENCIES ]
Update output: Already up to date / Updated / Error
\`\`\`

## 7. Suggested implementation files

Adjust to repository's current structure, keep file count reasonable:

- \`Views/YouTubeAudioDownloaderWindow.cs\` — Avalonia native UX.
- \`Services/YouTubeAudioService.cs\` — yt-dlp invocation, progress, cancellation, FFmpeg calls only if required (normally yt-dlp orchestrates conversion), metadata/output file path.
- \`Services/YtDlpDependencyService.cs\` — exact path selection, version, runtime/FFmpeg checks, **-U update operation** (may live in same service if simpler).
- \`Services/YouTubeAudioSettingsStore.cs\` — validated LocalAppData safe preferences.
- \`Tests/YouTubeAudioSmoke/\` — fake-executable tests for argument lists, progress, cancellation, updater, missing binary, pip-like configuration, safe path output.
- \`Services/ToolLauncher.cs\` — new tool catalog item in **Files**, accurate native status.
- \`Views/MainWindow.axaml.cs\` — explicit native tool launch case.
- Root README and this plan — status and usage.

Do not modify Seasonal Sprite Studio, Pixel Art Converter, Sprite Batch or their image service to add this downloader.

## 8. Scoped Codex phases

| Phase | Deliverable | Status |
| --- | --- | --- |
| **Y0 — contracts** | Confirm yt-dlp/FFmpeg/JS requirements, update-origin policy, URL and output safety, menu placement | **IMPLEMENTED — Windows validation pending** |
| **Y1 — download service** | Locate configured yt-dlp and FFmpeg; download a single audio-only YouTube URL to MP3 with safe arguments, cancellation, progress and smoke tests | **IMPLEMENTED — Windows validation pending** |
| **Y2 — Avalonia UI** | Register native Files tool, URL/folder/quality controls, progress, cancel, status/error log and Open Folder | **IMPLEMENTED — Windows validation pending** |
| **Y3 — manual updater** | **CHECK FOR UPDATES** runs \`-U\` on selected official release binary, pip/path guidance, version/exit status/log and concurrency guard | **IMPLEMENTED — Windows validation pending** |
| **Y4 — diagnostics/persistence** | Settings store, explicit installed-version/path display, FFmpeg and JS/EJS doctor, actionable warnings/failures | **NOT STARTED** |
| **Y5 — validation/refinement** | Windows functional tests, headless fake-process tests, documentation, responsive UX, limits, no regression | **NOT STARTED** |

**Practical request to Codex:** Y0/Y1 first for the core service, then Y2/Y3 together so the **update button is included in the initial usable UI**, then Y4/Y5. For a single Codex implementation request the user can authorize Y0–Y5 if desired, but all test claims must be genuine.

### Y0 / Y1 acceptance
- [ ] Exact selected binary path consistently used for download/update.
- [ ] Correct argv for MP3 single-URL download; no shell injection even with URLs containing \`&\` or malicious-looking strings.
- [ ] Progress/cancellation and FFmpeg-missing friendly diagnosis.
- [ ] Never silently rewrite or delete an existing MP3; collision-safe output plan.
- [ ] Fake process can simulate completed MP3, extractor failure, progress, cancellation, missing dependencies.
- [ ] Source ytdlp executable not bundled without explicit user permission.

### Y2 / Y3 acceptance — critical user scenario
- [ ] User can copy a YouTube link, choose folder and start download from native UI.
- [ ] When download stops working, user clicks **CHECK FOR UPDATES**.
- [ ] Update runs the **same exact exe** used by downloader (official standalone) and reports **version before/after** or "Already up to date".
- [ ] Update button disabled while downloading, and download disabled during updating.
- [ ] Broken internet, permission denial, updater unsupported, pip-managed or nonexistent executable produce accurate action-oriented errors, not falsely green status.
- [ ] Updating does not modify downloaded files or overwrite any user output.

### Y4 / Y5 acceptance
- [ ] Missing Deno/EJS warning is independent from yt-dlp version. Missing FFmpeg error is separately actionable.
- [ ] Settings persist through app relaunch; no secrets saved.
- [ ] On Windows: valid video succeeds (when permitted/available), final MP3 verified, no leftover unwanted intermediate on success.
- [ ] Update can be initiated after failed download without having to restart BEN TOOLBOX.
- [ ] Exit during active download cancels safely; no background orphan processes, UI deadlocks, or unbounded logs.
- [ ] Verified build: \`dotnet restore\`, \`dotnet build\`, native window opens, smoke tests pass; report actual Windows/CI outputs.
- [ ] No regressions to Game Development or other File Toolbox utilities.

## 9. Performance, usability and safety rules

- **No polling loops while idle.** Version/runtime detection runs on tool opening or on explicitly requested Refresh, not repeatedly per frame.
- UI updates event-driven and throttled; do not flood Avalonia dispatcher with every console line.
- One active process at a time in V1; process streams drained asynchronously to prevent pipe buffer deadlock.
- Preserve last folder and executable path; sensible default Music folder.
- External binaries are privileged to run locally; only launch user-selected or securely discovered trusted paths, show resolved path plainly, and never fetch/execute unverified binaries from untrusted URLs.
- Robust text decoding and stable UI when terminal emits warnings/logs; bounded debug log with optional Copy Diagnostics (redact URLs).
- Account for Windows path spaces, Unicode, illegal filename characters, locked files and duplicate titles.
- Errors should explain whether the problem is network, extractor, JS runtime, FFmpeg, permissions, destination, cancellation or update support.
- Respect applicable creator permissions, service terms and copyright; no protected-content bypass workflows.

## 10. Implementation log / first Codex prompt

- **2026-10-24 — PLAN CREATED.** Confirmed official \`yt-dlp -U\` is for release binary, package-managed installs differ; documented latest EJS/Deno/FFmpeg behavior, exact-exe update semantics, no-shell invocation, native UI/diagnostics and staged tests. **No application code or compiled build was performed.**

**Codex starter prompt:**

> Read BEN TOOLBOX 2.0's root README and \`Notes/Plans/YOUTUBE_AUDIO_DOWNLOADER_IMPLEMENTATION_PLAN.md\`. Implement phases Y0 through Y3 for a native **Files → YouTube Audio Downloader** (single YouTube URL → MP3) using the user's configured/local yt-dlp and FFmpeg. Include **CHECK FOR UPDATES** on the first working screen: \`-U\` for the exact selected standalone release executable, show versions and errors, and do not blindly update package-managed installations. Run fake-process smoke tests and actual .NET build where available. Respect no-shell injection, cancellation, protected output files and responsive Avalonia UI. Update documentation and commit. Leave Y4/Y5 enhancement/testing status honest.

### 2026-10-24 — Y0–Y3 implementation

Native Files catalog/routing, single-video MP3 service and UI, selected-executable updater with explicit standalone confirmation, bounded logs, throttled progress, async stdout/stderr drain, cancellation/process-tree termination, private output staging and non-overwriting collision handling implemented. User configuration is not hardcoded. Local paths/quality preferences are persisted as a small Y4 prerequisite; dependency discovery/version checks are basic, not a complete doctor or proof of EJS availability. Standalone confirmation is not persisted and resets when a different executable is selected.

`--ignore-config` isolates operations from user/global yt-dlp configuration. Official upstream README was checked for current update, audio extraction and progress/output conventions. No binaries were bundled/downloaded. No package-manager commands are executed. Failed/cancelled intermediates are retained and logged, not recursively deleted. Final completion requires successful exit, an existing nonempty staged MP3 and successful non-overwriting move. Reported MP3 presence is verified, not audio decoding or duration validity.

Validation performed on Linux .NET 10: main app build (0 errors; existing ImageSharp advisories/legacy warnings), fake-process tests (URL/argv, progress, output collision, extractor failure, rejected output escape, cancellation, busy guard, missing dependency/executable, updater confirmation/version refresh), and headless Avalonia constructor/catalog/compact-window checks. No real YouTube download or actual self-update was run; Windows functional/DPI/pointer/process-tree validation remains pending.

```powershell
dotnet build
dotnet run --project Tests/YouTubeAudioSmoke
dotnet run --project Tests/YouTubeAudioUiSmoke
```

The fake executable fixture requires Linux/python3; on Windows it runs URL/argv checks and explicitly reports the remaining fixture as skipped. The headless test does not validate actual Windows dialogs. For Windows acceptance select the user's existing exe via Browse (`D:\Ben Data\Music\MANUAL DOWNLOADS\YTDLP\yt-dlp.exe`), FFmpeg folder `C:\ffmpeg\bin`, then test a permitted video, cancellation/close, duplicate titles and manual update. Deno was not found in the user's PATH screenshot; report runtime warnings independently rather than implying updating fixes this.

**Follow-up:** At the user’s explicit request, Windows now defaults to `D:\Ben Data\Music\MANUAL DOWNLOADS\YTDLP\yt-dlp.exe`; saved nonempty selections still take precedence and Browse remains available. Pasted surrounding quotes are stripped before executable validation/launch, including previously saved quoted paths. Service smoke tests cover quoted paths and a quoted updater executable.
