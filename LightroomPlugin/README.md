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

1. Select one or more burst `.CR3` photos in the Library module.
2. **Library → Plug-in Extras → Extract Burst Frames**.
3. A progress dialog runs through the selection. Extracted frames are written
   to the same folder as each source CR3, then imported into the catalog and
   stacked under the source.
4. A summary dialog reports how many bursts and frames were processed.

Non-CR3 photos and single-frame CR3s in the selection are skipped (a
single-frame CR3 is not a burst, so the CLI writes one `_01.CR3` file but the
plugin leaves it on disk without importing — the source is already in the
catalog).

## Notes

- Windows only. The CLI is published as `win-x64` self-contained.
- The plugin shells out once per selected CR3. Cancelling the progress dialog
  stops between files (the in-flight extraction completes).
- If the CLI exits non-zero for a file, the file is recorded in the summary
  dialog's errors list and the rest of the selection still runs.
- The shipped exe is unsigned, so Windows SmartScreen may warn on first run.

## Cutting a release

1. Bump the `VERSION` block in
   `Cr3BurstExtractor.lrplugin/Info.lua` (e.g. `revision = 1`).
2. Commit the bump.
3. Tag the commit with `vX.Y.Z` matching the new version and push:
   ```
   git tag v1.0.1
   git push origin v1.0.1
   ```
4. The `Release Lightroom plugin` GitHub Actions workflow builds the zip on a
   Windows runner and publishes it as a GitHub Release with auto-generated
   notes. Users download the zip from the Releases page and follow the
   **Install** steps above.

The workflow refuses to publish if the tag and `Info.lua` version disagree,
so bump `Info.lua` before tagging.
