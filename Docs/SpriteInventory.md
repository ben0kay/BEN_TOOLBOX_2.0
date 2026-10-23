# Resource Checker — Sprite Inventory

Open **Game Development → Resource Checker** (the existing native Avalonia window).
Select your **Godot project root** (project.godot must exist).

The window has two separate tabs:

- **Resource Integrity** — the existing static res:// missing-resource checker,
  unchanged in Services/InspectorServices.cs.
- **Sprite Inventory** — a new, read-only inventory of PNG, WebP, JPEG, GIF
  and BMP files with filename, project-relative location, format, pixel
  dimensions and compressed file size.

Click **SCAN SPRITES** in Sprite Inventory. It displays total files, total
on-disk image size and file/directory errors without attempting to repair
anything. Use filename/path search, minimum **longest image edge**
in pixels, minimum file size in MiB, and sort by name, largest pixel area
or largest file size.

Selecting a row loads **one** scaled preview. Changing selection discards the
previous bitmap; other images are never opened for previews. The preview is
suppressed for images over 40 million pixels or files over 64 MiB to reduce
memory spikes. Some image types may not preview if the platform decoder cannot
display them, even when the header scanner can read their dimensions.

## Performance / safety contract

- Services/SpriteInventoryService.cs runs on a worker thread and reads only
  small image headers (for JPEG, up to 1 MiB searching frame markers). No
  ImageSharp full-image decoding is used during scanning.
- Header formats are PNG, WebP VP8/VP8L/VP8X, JPEG, GIF and BMP.
  Headers are not a full integrity/decode validation.
- The scan skips common generated folders: .git, .godot, .vs, .idea,
  bin, obj, node_modules. It avoids directory links/junctions, does
  not recurse outside the chosen tree and never writes to project files.
- A failed/unreadable image or directory is reported and does not terminate
  the scan. Cancellation discards incomplete results.
- The list has a fixed viewport so Avalonia can virtualize long lists.
  Only the selected preview is decoded (asynchronously, scaled to 320px
  width); previous previews are disposed. There is no gallery-wide eager
  thumbnail loading, persistent thumbnail database or gameplay-side cost.

## Testing

From the BEN_TOOLBOX_2.0 repository root on a PC with the .NET 10 SDK:

1. Run dotnet build
2. Run dotnet run --project Tests/SpriteInventorySmoke/SpriteInventorySmoke.csproj
3. Run dotnet run, then open Resource Checker and select a disposable or
   backed-up Godot project folder.
4. On **Resource Integrity**, scan known valid and missing res:// paths;
   verify the original results remain correct.
5. On **Sprite Inventory**, scan a real project. Verify count,
   dimension/file size labels, name/path filter, minimum edge and size
   thresholds, and both descending sort orders.
6. Select several PNG/WebP images: only one preview should show, and
   large images should display the safety notice. Test **CANCEL** during
   a scan and an intentionally truncated image in a disposable folder.

The test console writes synthetic temporary headers for the supported
formats and checks invalid files, excluded folders and cancellation;
it deletes its temporary directory on completion. The test project is
excluded from the normal Avalonia application's compile items.

**Limitations:** This is a visual asset inventory, not an unused-asset
finder, reference graph, image corruption validator or Godot import
compression profiler. Use **Asset Usage Finder** for reference tracing.
