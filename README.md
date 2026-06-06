# CR3 Burst Extractor

A small Windows desktop tool that pulls every frame out of a Canon **RAW Burst roll** (`CSI_*.CR3`) and writes each frame as a fully self-contained `.CR3` file that can be opened directly in Canon DPP, Adobe Lightroom, darktable and other RAW developers.

> **Compatibility — please read.** This tool has so far only been tested on burst rolls produced by the **Canon EOS R6 Mark II**. Other Canon bodies that record RAW burst rolls (R5, R3, R7 family, etc.) use the same ISOBMFF / CRX container layout in principle, but **compatibility on those cameras is unverified**. If you try it on another body, please open an issue and let me know whether it worked — sample files welcome.

---

## Why this exists

When you shoot a RAW burst on a Canon mirrorless body, the camera does not produce one `.CR3` per frame. Instead it writes a single container file (`CSI_*.CR3`) that holds **all the frames of the burst** in one ISOBMFF / CRX wrapper. Canon's own EOS Utility / Digital Photo Professional can split that container back into individual `.CR3` files, but:

- it is slow,
- it has to be done one roll at a time by hand,
- it is awkward to integrate into an offload / backup workflow.

`CR3 Burst Extractor` does the same split, in bulk, with a single click — recursively across a folder of burst rolls — and moves the originals into a backup folder so the workflow is non-destructive.

---

## What it does

Given a folder of `.CR3` files:

1. **Recursively scans** the folder for `*.CR3`.
2. For each file, inspects the `moov` sample tables to determine **how many frames** it contains.
3. If the file contains **more than one frame** (a burst roll):
   - Creates a sub-folder next to the original, named after the original file (without extension).
   - Writes each frame as a **standalone, valid `.CR3`** into that sub-folder.
   - Once all frames are written, **moves the original burst file into the Backup folder** you configured.
4. If the file contains **only one frame**, it is left untouched and logged as skipped (not a burst).
5. Files that are unreadable or malformed are logged as errors; the scan continues with the next file.

Files already located inside the Backup folder are ignored by the scan, so re-running over the same tree won't reprocess already-archived rolls.

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

- **Windows** (the app is built on WinForms)
- **.NET 10 SDK** (or the runtime, if you're only running pre-built binaries)
- No third-party dependencies; everything is in `System.*`.

---

## Building from source

```powershell
git clone https://github.com/rickardfalk199/cr3burstextractor.git
cd cr3burstextractor
dotnet build Cr3BurstExtractor/Cr3BurstExtractor.csproj -c Release
```

The built executable lands in:

```
Cr3BurstExtractor/bin/Release/net10.0-windows/Cr3BurstExtractor.exe
```

Run it directly, or:

```powershell
dotnet run --project Cr3BurstExtractor/Cr3BurstExtractor.csproj
```

---

## Using the application

### Splash screen

On first launch the application shows a splash screen with the logo, version, author info, the GitHub repo link, and a disclaimer. Tick **"Don't show this screen again"** before clicking **I understand – continue** to suppress the splash on subsequent launches. The setting lives in `%APPDATA%\Cr3BurstExtractor\skip_splash` — delete that file (or use a future menu toggle) to bring the splash back.

### Main window

| Field | Meaning |
| --- | --- |
| **Scan folder** | The folder to search recursively for `.CR3` files. |
| **Backup folder** | Original burst files are **moved** here after a successful extraction. |
| **Extract / Stop** | Click to start. While a run is in progress the button becomes **Stop** — pressing it halts the run **after the current file finishes** (no half-converted files left behind). |
| **Progress bar** | Shows files processed / total files found. |
| **Log box** | Per-file progress, including skipped files, the destination of each extraction, and error messages. |

### Help menu

- **Help → Help** (or **F1**): in-app description of what the application does.
- **Help → About**: the same info as the splash screen (logo, version, author, repo link, disclaimer) without the "don't show again" checkbox.

### Output layout

Given:

```
D:\Photos\Wedding\CSI_0001.CR3   (burst, 12 frames)
D:\Photos\Wedding\IMG_0008.CR3   (single frame)
```

after running with `Scan = D:\Photos\Wedding` and `Backup = D:\Photos\_burst_originals`, the tree becomes:

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

---

## Disclaimer

This software is provided **as is**, without warranty of any kind. The author takes **no responsibility** for any data loss, file corruption, or other damages resulting from its use.

**Use at your own risk.** Always keep a separate copy of irreplaceable burst rolls before running any third-party tool against them.

---

## Author / Contact

**Rickard Falk** &middot; [rickard.falk@outlook.com](mailto:rickard.falk@outlook.com)

Source code: [github.com/rickardfalk199/cr3burstextractor](https://github.com/rickardfalk199/cr3burstextractor)

Bug reports, sample burst files from other Canon bodies, and pull requests are all welcome.
