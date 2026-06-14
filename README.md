# CR3 Burst Extractor

A small Windows desktop tool that pulls every frame out of a Canon **RAW Burst roll** (`CSI_*.CR3`) and writes each frame as a fully self-contained `.CR3` file that can be opened directly in Canon DPP, Adobe Lightroom, darktable and other RAW developers.

> **Compatibility — please read.** This tool has so far only been tested on burst rolls produced by the **Canon EOS R6 Mark II**. Other Canon bodies that record RAW burst rolls (R5, R3, R7 family, etc.) use the same ISOBMFF / CRX container layout in principle, but **compatibility on those cameras is unverified**. If you try it on another body, please open an issue and let me know whether it worked — sample files welcome.

---

## Why this exists

When you shoot a RAW burst on a Canon mirrorless body, the camera does not produce one `.CR3` per frame. Instead it writes a single container file (`CSI_*.CR3`) that holds **all the frames of the burst** in one ISOBMFF / CRX wrapper. Canon's own EOS Utility / Digital Photo Professional can split that container back into individual `.CR3` files, but:

- it is slow,
- it has to be done one roll at a time by hand,
- it is awkward to integrate into an offload / backup workflow.

`CR3 Burst Extractor` does the same split, in bulk, with a single click — recursively across a folder of burst rolls — and moves the originals into a backup folder so the workflow is non-destructive. Since v0.5 it can also run as a **Windows Service** that watches your scan folder and auto-extracts new bursts as they appear (e.g. straight off a card reader), with an optional Windows toast notification per burst.

---

## What it does

Given a folder of `.CR3` files:

1. **Recursively scans** the folder for `*.CR3`.
2. For each file, inspects the `moov` sample tables to determine **how many frames** it contains.
3. If the file contains **more than one frame** (a burst roll):
   - Creates a sub-folder next to the original, named after the original file (without extension).
   - Writes each frame as a **standalone, valid `.CR3`** into that sub-folder.
   - Once all frames are written, **moves the original burst file into the Backup folder** you configured.
4. If the file contains **only one frame**, it is left untouched and logged as skipped (not a burst). The result is remembered so subsequent scans don't re-open the file.
5. Files that are unreadable or malformed are logged as errors; the scan continues with the next file.

Files already located inside the Backup folder are ignored by the scan, so re-running over the same tree won't reprocess already-archived rolls.

The same per-file logic powers both the on-demand **Extract** button and the background service.

---

## How the extraction works (short version)

A burst roll is an ISOBMFF container whose `moov` box holds N tracks, each containing a sample table that points into one or more `mdat` blobs. Each sample in the image tracks (JPEG thumbnail / CRX-small preview / CRX-big RAW) corresponds to one frame in the burst.

To produce a standalone `.CR3` for a single frame, the tool:

1. Parses the box tree and keeps the raw byte range of every box.
2. Clones the top-level `moov` box and **patches every `co64` / `stco` offset table** so each offset points into the new file's layout (`moov` first, then `mdat` immediately after).
3. Rewrites `stsz` so it contains exactly one sample entry (the frame being extracted) and `stsc` / `stts` accordingly.
4. Carries over the top-level `uuid` boxes (XMP / PRVW preview / CMTA) and re-points the `CTBO` table inside `moov` — without this, `CTBO` would point past EOF and Adobe / DPP would refuse to decode the RAW even though the preview shows correctly.
5. Writes the final file as:  `ftyp` | patched-`moov` | `mdat( frame bytes )`.

The result is byte-for-byte valid as far as the standard CR3 readers are concerned.

---

## Requirements

- **Windows** (the app is built on WinForms; the service uses Win32 session APIs)
- **.NET 10 SDK** (or the runtime, if you're only running pre-built binaries)
- Two NuGet dependencies: `Microsoft.Extensions.Hosting` + `Microsoft.Extensions.Hosting.WindowsServices` (used only by the service mode) and `System.Drawing.Common` (used for per-frame thumbnail re-encoding).

---

## Building from source

```powershell
git clone https://github.com/rickardfalk199/cr3burstextractor.git
cd cr3burstextractor
dotnet build Cr3BurstExtractor.sln -c Release
```

The standalone exe lands in:

```
Cr3BurstExtractor/bin/Release/net10.0-windows/Cr3BurstExtractor.exe
```

To run the form directly:

```powershell
dotnet run --project Cr3BurstExtractor/Cr3BurstExtractor.csproj
```

For a self-contained single-file build of both the standalone exe and the CLI (the same artifacts the release workflow ships):

```powershell
.\LightroomPlugin\build-plugin.ps1
```

---

## Using the application

### Splash screen

On first launch the application shows a splash screen with the logo, version, author info, the GitHub repo link, and a disclaimer. Tick **"Don't show this screen again"** before clicking **I understand – continue** to suppress the splash on subsequent launches. The setting lives in `%ProgramData%\Cr3BurstExtractor\settings.json` (`SkipSplash: true`); delete that file to reset to defaults. Existing installs that stored settings in `%APPDATA%\Cr3BurstExtractor\` are migrated automatically on first launch.

### Main window

| Field | Meaning |
| --- | --- |
| **Scan folder** | The folder to search recursively for `.CR3` files. |
| **Move originals to backup folder** (checkbox) | When **ticked**, original burst files are moved into the Backup folder after a successful extraction. When **unticked**, the burst file is left in its original directory, next to the sub-folder containing its extracted frames. |
| **Backup folder** | Only used when the checkbox above is ticked. Disabled (greyed out) otherwise. |
| **Extract / Stop** | Click to start. While a run is in progress the button becomes **Stop** — pressing it halts the run **after the current file finishes** (no half-converted files left behind). |
| **Progress bar** | Shows files processed / total files found. |
| **Log box** | Per-file progress, including skipped files, the destination of each extraction, and error messages. |

### Settings menu

- **Auto-extract new files in scan folder** — when ticked, the background service watches the scan folder and runs the same Extract logic on each new `.CR3` as it appears. Requires the service to be installed and running (see the Service menu). The form will prompt you if the service isn't running yet.
- **Show Windows notification on auto-extract** — when ticked, a Windows toast (`"Extracted N frames from xyz.CR3"`) pops once per successful auto-extraction. Default on.

### Service menu

Manage the background service directly from the form. Each item shells out to `Cr3BurstExtractor.exe` with the matching flag and elevates via UAC where required.

- **Install service…** — creates the `Cr3BurstExtractor` Windows Service. Disabled when already installed.
- **Uninstall service…** — stops the service if running and removes it.
- **Start service** / **Stop service** — control the running state. The menu items enable/disable based on the live state when you open the menu.

### Help menu

- **Help → Help** (or **F1**): in-app description of what the application does.
- **Help → About**: the same info as the splash screen (logo, version, author, repo link, disclaimer) without the "don't show again" checkbox.

### Output layout

Given:

```
D:\Photos\Wedding\CSI_0001.CR3   (burst, 12 frames)
D:\Photos\Wedding\IMG_0008.CR3   (single frame)
```

**With** the "Move originals to backup folder" checkbox **ticked**, running with `Scan = D:\Photos\Wedding` and `Backup = D:\Photos\_burst_originals` produces:

```
D:\Photos\Wedding\
    CSI_0001\
        CSI_0001_01.CR3
        CSI_0001_02.CR3
        ...
        CSI_0001_12.CR3
    IMG_0008.CR3                   (skipped, untouched)

D:\Photos\_burst_originals\
    CSI_0001.CR3                   (the moved original)
```

If a name collision would occur in the backup folder, the moved file gets a `_1`, `_2`, ... suffix.

**With** the checkbox **unticked**, the same scan produces (no backup folder used):

```
D:\Photos\Wedding\
    CSI_0001.CR3                   (the original — left in place)
    CSI_0001\
        CSI_0001_01.CR3
        CSI_0001_02.CR3
        ...
        CSI_0001_12.CR3
    IMG_0008.CR3                   (skipped, untouched)
```

---

## Background service (auto-extract)

The same exe doubles as a Windows Service. Once installed and started, it watches the scan folder (recursively) for new `.CR3` files and runs the same per-file extraction logic the **Extract** button does. Typical workflow: point your camera offload at the scan folder, plug in a card, walk away — bursts are split as they land and you get a Windows toast per roll.

### Setup

1. In the form, set the **Scan folder** (and **Backup folder** if you want originals moved). Save by closing the form, or just clicking Extract once.
2. **Service → Install service…** (prompts for UAC).
3. **Settings → Auto-extract new files in scan folder** — tick it. If the service isn't running yet, the form will prompt you.
4. **Service → Start service**.

The service then runs as `LocalSystem` and starts automatically with Windows. Toggling the Settings checkbox at any time takes effect immediately — the service tails `settings.json` and rebinds without a restart.

### Behavior

- Watches `Scan folder` recursively for new `*.CR3`.
- Waits for the file to be fully written (camera offload / card-reader copy can take seconds for large bursts) before processing — the service tries to open it with no sharing and retries for up to 60 seconds.
- Runs the same Extract logic as the form: cache check → frame count → extract or mark as single-frame → optionally move original to Backup folder.
- On successful burst extraction, pops a Windows toast in the logged-in user's session (`Extracted N frames from xyz.CR3`). Can be disabled via Settings menu.
- Logs everything to `%ProgramData%\Cr3BurstExtractor\service.log` (5 MB rolling) and to the Windows Event Log under source `Cr3BurstExtractor`.

### Console subcommands

The same flags the Service menu uses are available from a terminal — useful for scripting or remote management:

```powershell
Cr3BurstExtractor.exe --install      # install the service (elevates via UAC)
Cr3BurstExtractor.exe --start
Cr3BurstExtractor.exe --stop
Cr3BurstExtractor.exe --status       # sc query Cr3BurstExtractor
Cr3BurstExtractor.exe --uninstall
Cr3BurstExtractor.exe --service      # used by sc.exe itself; not for manual use
```

### Limitations

- The notification toast only reaches the active console session — RDP-only or logged-out machines won't show a toast (the extraction still happens; check the log).
- `FileSystemWatcher` reliability over SMB / UNC paths is limited. The service logs a warning if `Scan folder` is a UNC path but will still try.

---

## Companion tools

This repository also ships:

- **`Cr3BurstExtractor.Cli.exe`** — a no-UI CLI for scripts and pipelines. `Cr3BurstExtractor.Cli.exe <input.cr3> [output-dir]`, `--count-only`, and `--get-scan-folder` / `--set-scan-folder` for sharing config with the standalone tool.
- **Lightroom Classic plugin** — adds an *Extract Burst Frames* action under *Library → Plug-in Extras* that splits selected burst CR3s and imports the per-frame files back into the catalog, stacked under the source burst. See [`LightroomPlugin/README.md`](LightroomPlugin/README.md).
- **`Cr3BurstExtractor.Lib`** — the underlying library. All CR3 parsing, frame building, and box-tree patching lives here, with a stream-based primary API (`BurstReader.Open(Stream)` → `FrameCount` / `ExtractFrame(int, Stream)`). Path-based wrappers (`BurstExtractor.GetFrameCount`, `BurstExtractor.Extract`) are kept for callers that prefer them. The library has no UI dependencies and can be referenced from any .NET project that needs to consume or produce CR3 burst files.

---

## Disclaimer

This software is provided **as is**, without warranty of any kind. The author takes **no responsibility** for any data loss, file corruption, or other damages resulting from its use.

**Use at your own risk.** Always keep a separate copy of irreplaceable burst rolls before running any third-party tool against them.

---

## Author / Contact

**Rickard Falk** &middot; [rickard.falk@outlook.com](mailto:rickard.falk@outlook.com)

Source code: [github.com/rickardfalk199/cr3burstextractor](https://github.com/rickardfalk199/cr3burstextractor)

Bug reports, sample burst files from other Canon bodies, and pull requests are all welcome.
