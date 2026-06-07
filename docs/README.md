# CR3 Burst Format Documentation

This is the technical reference for the Canon CR3 RAW Burst container format, written for anyone working on **CR3 Burst Extractor** or wanting to understand the file layout in enough detail to read, modify, or recreate burst rolls.

Most of the details here are derived from:

- The reference work at [`lclevy/canon_cr3`](https://github.com/lclevy/canon_cr3), which reverse-engineered the format for non-burst CR3 files.
- Diagnostic output from the test suite in this repository (`Cr3BurstExtractor.Tests`), which dumps box trees and byte-level diffs against Canon DPP-extracted reference frames.
- The production extraction code in `Cr3BurstExtractor/Managers/`, particularly `BurstExtractor`, `FrameBuilder`, `MoovBuilder`, `ThmbBuilder`, `PrvwBuilder`.
- Verified behaviour on a **Canon EOS R6 Mark II** (`L:\Canon R6 mk2` test data).
  Behaviour on other Canon bodies is unverified.

## Reading order

| # | Document | What it covers |
| --- | --- | --- |
| 1 | [01-overview.md](01-overview.md) | What a CR3 burst roll is, how it differs from a normal CR3, and why extraction is non-trivial |
| 2 | [02-isobmff-primer.md](02-isobmff-primer.md) | ISO BMFF container basics: boxes, sizes, byte order, nesting |
| 3 | [03-file-structure.md](03-file-structure.md) | Top-level box layout of a CR3 file (ftyp, moov, uuid boxes, mdat) |
| 4 | [04-sample-tables.md](04-sample-tables.md) | How `stbl` and its children (`stsz`, `stsc`, `stts`, `co64`/`stco`) describe per-track samples and reference `mdat` |
| 5 | [05-previews-and-metadata.md](05-previews-and-metadata.md) | THMB, PRVW, EXIF IFD1 thumbnail; CMT1/CMT2/CMT3/CMT4 metadata boxes; what each holds and where they live |
| 6 | [06-extraction-and-dpp-parity.md](06-extraction-and-dpp-parity.md) | How our extractor builds a single-frame CR3, and where it currently differs from Canon DPP output |
| 7 | [07-spec-conformance.md](07-spec-conformance.md) | Point-by-point conformance check against the [lclevy/canon_cr3](https://github.com/lclevy/canon_cr3) reference specification |

## TL;DR — the format in 60 seconds

A CR3 burst roll is an ISOBMFF (MP4-family) container. The structure is roughly:

```mermaid
graph LR
    A[ftyp<br/>brand: crx ] --> B[moov<br/>headers + tracks]
    B --> C[uuid<br/>XMP]
    C --> D[uuid<br/>PRVW preview]
    D --> E[uuid<br/>Canon 5766b829...]
    E --> F[free<br/>padding]
    F --> G[mdat<br/>all frame samples<br/>contiguously]
```

Inside `moov` there is a Canon-specific `uuid` box (`85c0b687-820f-11e0-8111-f4ce462b6a48`) that wraps the EXIF / MakerNote / GPS / thumbnail boxes (`CMT1`, `CMT2`, `CMT3`, `CMT4`, `THMB`, plus a few control structures `CNCV`, `CCTP`, `CTBO`, `CNOP`). After that wrapper come the per-track `trak` boxes — typically four of them: a JPEG preview track, a CRX-small preview track, a CRX-big RAW track, and a small metadata track.

A **burst roll** is just a normal CR3 with **N samples per track** instead of 1 (one sample = one frame). Extracting a single frame means cloning `moov` with each track's sample tables trimmed to a single sample, copying that frame's bytes into a fresh `mdat`, and patching all of the offset tables (`co64`/`stco` and the `CTBO` top-level offset table) so they point at the new layout.

For full detail, start with [01-overview.md](01-overview.md).
