# CR3 Burst Extractor — Lightroom Classic Plugin

Adds an **Extract Burst Frames** action under **Library → Plug-in Extras** that
splits the selected Canon burst CR3s into per-frame CR3 files and imports them
back into the catalog, stacked under the source burst.

## Build

From PowerShell, in this folder:

```powershell
.\build-plugin.ps1
```

This publishes the CLI as a self-contained single-file exe and drops it into
`Cr3BurstExtractor.lrplugin\bin\`.

## Install

1. In Lightroom Classic: **File → Plug-in Manager → Add**.
2. Select the `Cr3BurstExtractor.lrplugin` folder (not a file inside it).
3. The plugin should appear as enabled in the list.

## Use

Burst CR3s are not supported by Lightroom, so they typically aren't in your
catalog at all — the plugin operates on the **filesystem**, not the catalog,
matching how the standalone WinForms tool works.

1. **File → Plug-in Extras → Extract Burst Frames…**
   (in Swedish: **Arkiv → Plugin-tillbehör → Extract Burst Frames…**).
2. A folder picker opens, pre-populated with the last folder used by either
   the plugin or the WinForms tool (they share `%APPDATA%\Cr3BurstExtractor\settings.json`).
   Pick a folder containing burst `.CR3` files.
3. The plugin recursively scans the folder, runs the CLI on every `.CR3`
   found, and shows a progress dialog (cancelable).
4. Extracted frames are written next to each source burst, then imported
   into Lightroom and stacked together (first frame on top).
5. A summary dialog reports how many bursts and frames were processed.

Single-frame CR3s are skipped cheaply via the cache (shared with the WinForms
tool), so re-scanning a folder is fast and doesn't produce duplicate output.

## Notes

- Windows only. The CLI is published as `win-x64` self-contained.
- The plugin shells out once per selected CR3. Cancelling the progress dialog
  stops between files (the in-flight extraction completes).
- If the CLI exits non-zero for a file, the file is recorded in the summary
  dialog's errors list and the rest of the selection still runs.
- The shipped exe is unsigned, so Windows SmartScreen may warn on first run.

## Cutting a release

The product version lives in **`Cr3BurstExtractor/AppInfo.cs`** (`Version`
constant) and is the single source of truth — `build-plugin.ps1` patches
`Info.lua`'s VERSION block from it so the .exe and the Lightroom Plug-in
Manager always agree.

1. Bump `Version = "..."` in `Cr3BurstExtractor/AppInfo.cs` (e.g. `"0.4"` or
   `"0.3.1"` — `major.minor[.revision[.build]]`).
2. Commit the bump.
3. Tag the commit with `vX.Y.Z` matching `AppInfo.Version` exactly and push:
   ```
   git tag v0.4
   git push origin v0.4
   ```
4. The `Release Lightroom plugin` GitHub Actions workflow builds the zip on a
   Windows runner and publishes it as a GitHub Release with auto-generated
   notes. Users download the zip from the Releases page and follow the
   **Install** steps above.

The workflow refuses to publish if the tag and `AppInfo.Version` disagree,
so bump `AppInfo.cs` before tagging.
