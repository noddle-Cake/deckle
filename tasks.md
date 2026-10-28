# Scanwise: work plan

One cross-platform desktop scanner app. It combines:

- **simon-knuth/scanner** (UWP, MPL-2.0): the simple Fluent UI, auto-rotate,
  custom save location and file naming, and its save formats.
- **NAPS2** (official `cyanfish/naps2`, GPL/LGPL): the scanning engine and the
  features Scanner lacks.

Where both apps do something, the better version wins. Each such choice is
recorded under "Decisions".

## Source analysis

### Provenance warning

The NAPS2 link in the request (`github.com/naps2-software/naps2-scanner`) is
**not** the official project. It is a flattened partial copy with:

- a keyword-stuffed README ("Scanner Vocabulary"),
- a "Get NAPS2 Scanner" download badge pointing to a github.io page,
- a 300 KB `404.html` that loads a remote endpoint and then runs
  `location.replace()` to it.

That is the usual malware-lure pattern. **Nothing is taken from it**: no code,
no binaries, no links. NAPS2 features come from the official, signed NuGet
packages `NAPS2.Sdk` 1.6.0 and `NAPS2.Images.ImageSharp` 1.6.0 (LGPL-2.1,
published from github.com/cyanfish/naps2). Its source file list was used only
as a feature checklist, and it matches upstream NAPS2.

### simon-knuth/scanner: what it has

| Area | Details |
|---|---|
| Platform | UWP, Windows only, WIA only (`Windows.Devices.Scanners`) |
| UI | NavigationView left-compact rail: Scan options · Pages · Editor; footer: Save location, Help, Settings. Adaptive: wide = 3 panes, narrow = top nav. Win11 Fluent look. |
| Scan options | Scanner dropdown with live discovery; source Auto/Flatbed/Feeder; file format; color Color/Gray/Mono/Auto; resolution; feeder "multiple pages" + duplex; auto-crop; brightness/contrast with reset; Preview + region select; split Scan button (Scan / Add to document / Start fresh / Merge) |
| Formats | PDF, JPG, PNG, TIF, BMP |
| Auto-rotate | OCR at 0/90/180/270°, keep the rotation with the most recognized text; ignored below a 50-char threshold |
| Saving | Save location: fixed folder **or ask every time**; naming: DateTime / Date / **custom pattern** made of blocks (text, year, month, day, hour, AM/PM, minute, second, resolution, brightness, contrast, scanner name, file type) |
| Editor | Crop with aspect-ratio presets (square, 3:2, 4:3, DIN A, ANSI A/B/C, Legal, Kai 4/8/16/32, flip); "crop similar pages"; rotate 90/180/270; draw/highlight (Ink); rename; delete; save / save copy; zoom; open with; share; copy |
| Page list | Multi-select; rotate; delete; duplicate; export page; copy; show in folder; drag reorder |
| Scan merge | Insert a second batch between existing pages (manual duplex), with reverse order |
| Settings | Theme, save location, auto-rotate language, naming pattern, scan action (add vs start fresh), remember options, units (metric/imperial), animations |

### NAPS2: features Scanner lacks

| Area | Details |
|---|---|
| Platforms | Windows, macOS, Linux |
| Drivers | WIA, TWAIN, SANE, ESCL/AirScan (driverless network), Apple ICA |
| Profiles | Named, reusable scan settings (device, dpi, size, source, color, …) |
| Scan processing | Auto-deskew, blank-page exclusion (white/coverage thresholds), page size presets + custom, brightness/contrast, max quality/JPEG quality, flip duplexed pages |
| OCR | Tesseract, 100+ languages, searchable PDFs |
| PDF | Metadata, encryption with permissions, PDF/A-1b/2b/3b/3u, compression |
| TIFF | Multi-page TIFF, compression choice |
| Import | Images and existing PDFs into the page list |
| Batch scan | Multiple scans with delay or prompt, output single file or per-scan files |
| Image adjustments | Brightness, contrast, hue, saturation, sharpen, grayscale, black & white threshold |
| Page ops | Interleave / deinterleave, alt-interleave, reverse order, move up/down, undo |
| Error handling | Typed device errors (paper jam, feeder empty, cover open, busy, offline, warming up) |

## Decisions

| Topic | Decision | Why |
|---|---|---|
| ImageSharp | Pinned to 3.1.12 (what NAPS2 1.6 needs). TIFF **decoding and encoding** both go through LibTiff.NET, so the advisories against ImageSharp's TIFF code can't be reached. | 3.2+ fixes them but fails Release builds without a Six Labors license key. See docs/SECURITY-NOTES.md. |
| Language/runtime | C# / .NET 8 (`net8.0`); a one-line switch to `net10.0` in `Directory.Build.props` | NAPS2.Sdk is .NET; only the .NET 8 SDK is installed (sudo is needed for 10). .NET 8 support ends Nov 2026, so **retarget to net10 as soon as the SDK is installed**. |
| UI toolkit | Avalonia 11.2.8 + FluentAvaloniaUI 2.4.1 (NavigationView, InfoBar, ContentDialog, Fluent symbol icons) | Closest cross-platform match to Scanner's WinUI/Fluent look. FA 2.5+/3.x require net10. |
| MVVM | CommunityToolkit.Mvvm | Scanner already uses the same toolkit (old name). |
| Scan engine | NAPS2.Sdk (`ScanController`) with `ImageSharpImageContext` everywhere | One managed image backend on every OS. Verified: SANE `test` backend → scan → PDF works on this host. |
| Drivers per OS | Linux: SANE + ESCL. Windows: WIA + TWAIN (via `NAPS2.Sdk.Worker.Win32`) + ESCL. macOS: SANE (`NAPS2.Sane.Binaries`) + ESCL; Apple ICA needs `net8.0-macos` and is a stretch task. | |
| Auto-rotate | **Tesseract OSD (`--psm 0`) first** when `osd.traineddata` exists: one pass, and it reports confidence. Fallback: Scanner's 4-way OCR text count via NAPS2's Tesseract engine. Runs **after** NAPS2 auto-deskew (fine angle), so the two together fix any orientation. | Better than Scanner's: cross-platform, 4× faster with OSD, and adds deskew. |
| Formats | PDF, JPG, PNG, TIF (multi-page), BMP. Also searchable PDF (OCR), PDF/A. | Scanner's set, plus NAPS2's extras. ImageSharp context has no TIFF writer, so TIFF is written with ImageSharp directly. JPEG 2000 and XPS are dropped (no cross-platform encoder; XPS is obsolete). |
| Save model | Scanner's model: save location = fixed folder **or** ask every time; **auto-save after each scan** (on by default, like Scanner), plus explicit "Save as…" for the whole list or the selection | Scanner's flow is simpler; NAPS2's "save as" covers the rest. |
| Single vs. separate files | Image formats → one file per page; PDF/TIFF → one multi-page file. Option: "PDF per scan" vs "add to current PDF" (Scanner's Scan / Add / Start fresh split button). | |
| File naming | Token string, edited with Scanner-style chips: `{year} {month} {monthname} {day} {hour} {hour12} {ampm} {minute} {second} {scanner} {dpi} {color} {source} {brightness} {contrast} {n}`, plus free text. Presets: DateTime (default), Date, Custom. Collisions get ` (2)`, ` (3)`… | Scanner's block set plus NAPS2's counter, stored as a readable string rather than Scanner's `*BLOCK\|…` serialization. |
| Profiles | NAPS2 profiles kept, but shown Scanner-style: the scan pane **is** the active profile. A small profile dropdown at the top with Save as / Rename / Delete. The last-used options are always remembered. | Keeps Scanner's simplicity and NAPS2's power. |
| Editor | Scanner's editor (crop with aspect presets, rotate, delete, zoom) + NAPS2 adjustments (brightness/contrast/hue/saturation/sharpen/grayscale/B&W, deskew). Draw/highlight is a later phase. | |
| Not ported | Share/Open-with Store dialog, AppCenter telemetry, surveys, donate dialog, UWP printing | Windows-only or not wanted. Printing → "Open in default app". |
| Persistence | `settings.json` + `profiles.json` in the OS config dir (`%APPDATA%/Scanwise`, `~/Library/Application Support/Scanwise`, `$XDG_CONFIG_HOME/scanwise`) | |
| Session recovery | Pages live in NAPS2's file storage under the app data dir. The page list is restored on next start (NAPS2-style recovery). | |
| License | MPL-2.0 (same as Scanner). Dependencies: NAPS2.Sdk LGPL-2.1, Avalonia MIT, FluentAvalonia MIT, ImageSharp Six Labors Split (Apache-2.0 for OSS). No source copied from either app; the UI is re-implemented. | |
| Name | **Scanwise** | Neutral name that avoids both trademarks. |
| Commits | Authored as the repo owner, no Claude attribution | User instruction. |

## Architecture

```
Scanwise.sln
├── Directory.Build.props          TargetFramework, nullable, versions
├── src/Scanwise.Core              no UI; fully unit-tested
│   ├── Scanning/                  ScannerService (discovery, caps, scan), ScanSettings, Profiles
│   ├── Pages/                     PageDocument (ordered pages, selection-free ops, undo)
│   ├── Processing/                AutoRotateService, Tesseract locator
│   ├── Export/                    ExportService (pdf/jpg/png/tif/bmp), TiffWriter, FileNamer
│   ├── Import/                    ImportService (images, pdf)
│   ├── Settings/                  AppSettings, SettingsStore (JSON), AppPaths
│   └── Batch/                     BatchScanner
├── src/Scanwise.App               Avalonia UI
│   ├── Views/                     ShellWindow, ScanOptionsView, PageListView, EditorView, SettingsView, dialogs
│   ├── ViewModels/
│   └── Assets/
└── tests/Scanwise.Core.Tests      xUnit, plus SANE test-backend integration tests
```

## Tasks

Status: `[ ]` todo · `[x]` done · `[~]` partial/deferred (see note)

### Phase 0: Setup
- [x] Analyze both sources; verify NAPS2 provenance; pick stack
- [x] Probe NAPS2.Sdk on this host: SANE test scan → PDF OK; ImageSharp context lacks TiffWriter
- [x] `git init`, .gitignore, .editorconfig, LICENSE (MPL-2.0), README
- [x] Solution + 3 projects + Directory.Build.props; builds clean with warnings as errors in Core

### Phase 1: Core
- [x] `AppPaths` (config/data/temp dirs per OS) and `SettingsStore` (atomic JSON write, defaults, schema version)
- [x] `AppSettings`: theme, save location mode + folder, default format, naming pattern, auto-rotate on/off + OCR language, auto-save, scan action (add/fresh), units, remember options, OCR on PDF, PDF options (metadata, PDF/A, encryption), JPEG quality, TIFF compression
- [x] `ScanSettings` (one profile): device, driver, source, color, dpi, page size (+custom), brightness, contrast, auto-deskew, exclude blank pages + thresholds, quality, duplex flip. `ToScanOptions()`.
- [x] `ProfileStore`: list, default, save/rename/delete, "last used"
- [x] `ScannerService`: per-OS driver list; streaming discovery across drivers (dedupe); caps → UI capability model (sources, dpi list, color modes, page area); scan as `IAsyncEnumerable` with page progress + cancel; map NAPS2 exceptions to friendly messages
- [x] `PageDocument`: ordered `ProcessedImage` list; add/insert/remove/move/duplicate/reverse/interleave/deinterleave/alt-interleave/merge-at (Scanner's scan merge); rotate/crop/adjust via NAPS2 transforms; undo/redo stack; change events; disposal
- [x] `AutoRotateService`: OSD path + 4-way fallback; min-confidence threshold; never throws (returns 0°)
- [x] `TesseractLocator`: system `tesseract` on PATH / known dirs, bundled binaries (NAPS2.Tesseract.Binaries) as fallback, tessdata dir discovery, available languages, `osd` presence; optional download of `*.traineddata` from tesseract-ocr/tessdata_fast into app data
- [x] `FileNamer`: token parser/renderer, preset patterns, invalid-char sanitizing, collision suffixing, preview text
- [x] `ExportService`: PDF (NAPS2 PdfExporter + OCR + metadata + PDF/A + encryption), TIFF multi-page (ImageSharp), JPG/PNG/BMP per page; progress + cancel; returns written paths
- [x] `ImportService`: images (incl. multi-frame TIFF) + PDF (pdfium binaries) into the PageDocument
- [x] `BatchScanner`: N scans, delay or prompt between, output single/per-scan, honors naming + auto-save
- [x] `SessionStore`: persist the page list (recovery) on change; restore on start

### Phase 2: Tests
- [x] FileNamer tokens, sanitizing, collisions
- [x] Settings/profile round-trip, corrupt-file fallback to defaults
- [x] PageDocument ops incl. interleave/merge/undo
- [x] Export: each format writes a valid file (decode back; page count for PDF/TIFF)
- [x] AutoRotate: render text, rotate 90/180/270, detect the fix (skipped if no tesseract)
- [x] SANE integration: discover `test` device, scan flatbed + feeder (skipped if no libsane)

### Phase 3: UI shell (Scanner look)
- [ ] App bootstrap: DI container, FluentAvaloniaTheme, theme from settings (system/light/dark), single-window
- [ ] Shell: left-compact NavigationView: Scan · Pages (badge = page count) · Editor; footer: Open save folder, Help, Settings. Adaptive layout: ≥1100px 3 panes (options | pages | editor), ≥775px 2 panes, narrow = one pane with top nav (Scanner's breakpoints).
- [ ] Status InfoBar + progress ("Scanning page 2…", "Auto-rotating…", "Saving PDF…") with cancel
- [ ] Error dialog with friendly device-error text + "Get help" link

### Phase 4: Scan options pane
- [ ] Profile dropdown (Save as new / Rename / Delete)
- [ ] Scanner dropdown with live discovery, refresh, driver badge (SANE/ESCL/WIA/TWAIN), "no scanner found" help
- [ ] Source radio buttons (only supported ones enabled), feeder: duplex
- [ ] Format: PDF / JPG / PNG / TIF / BMP
- [ ] Color: Color / Grayscale / Black & white (only supported)
- [ ] Resolution dropdown from caps with "recommended" hint (300 for text)
- [ ] Page size: Auto/A4/Letter/Legal/A5/A3/B5/B4/Custom
- [ ] Expander "More options": brightness/contrast sliders with reset, auto-deskew, auto-rotate, skip blank pages, OCR (searchable PDF) + language
- [ ] Split Scan button: Scan · Add to current document · Start fresh · Merge into existing pages (Scanner's scan merge dialog: insert position, reverse order) · Batch scan…

### Phase 5: Pages + editor
- [ ] Page list: thumbnail grid, multi-select (ctrl/shift), drag-drop reorder, drop files to import, context menu (rotate, delete, duplicate, move, export selected, open in default app); toolbar: select all, rotate, delete, reverse, interleave/deinterleave, import, save as
- [ ] Undo/redo (Ctrl+Z/Ctrl+Y) wired to PageDocument
- [ ] Editor: large preview with zoom/pan, prev/next, rotate 90/180/270, crop with handles + aspect presets (Scanner's list, with flip) + "apply to similar pages", adjustments panel (brightness, contrast, hue, saturation, sharpen, grayscale, B&W threshold) with live preview, deskew, delete, save
- [~] Draw/highlight (pen, highlighter, eraser, colors) burned into the image: phase 7 stretch

### Phase 6: Settings + saving flow
- [ ] Settings view (Scanner layout, grouped cards): Save location (folder / ask every time + browse + open), File naming (presets + custom chip editor with live preview), Auto-rotate (toggle + OCR language), After scan (auto-save, add vs start fresh), PDF (OCR, PDF/A, metadata, password), Image quality (JPEG quality, TIFF compression), Appearance (theme), About/licenses
- [ ] Save flow: auto-save after scan per settings; "Save as…" (file picker with format filter); success InfoBar with "Open file"/"Show in folder"
- [ ] Batch scan dialog
- [ ] Help view: short topics (scanner not found, choosing resolution, formats, scan merge, OCR languages)

### Phase 7: Polish
- [ ] Keyboard shortcuts (Ctrl+N scan, Ctrl+S save, Ctrl+O import, Del, Ctrl+A, Ctrl+Z/Y, R rotate)
- [ ] App icon, window title with page count
- [ ] Session recovery prompt on start
- [~] Draw/highlight tool
- [~] Apple ICA driver (`net8.0-macos` TFM) — needs a Mac to build/test

### Phase 8: Ship
- [ ] Headless UI smoke test (Avalonia.Headless): shell opens, navigation works, scan via SANE test device adds pages, save writes a file; render screenshots to verify layout
- [ ] `scripts/publish.sh`: self-contained single-folder builds for linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64
- [ ] README: features, install, build, OS notes (SANE packages, Tesseract languages), credits to Scanner and NAPS2
- [ ] Final review pass (`/code-review`-style self review), all tests green, commit

## Verification log

- 2026-10-28: Core test suite: 50/50 passing (`scripts/test.sh`, with `SCANWISE_TEST_OSD_DIR` set). Covers the
  SANE test device (flatbed, feeder, region crop), OSD and text-comparison auto-rotate at 0/90/180/270, OCR PDF,
  encrypted PDF, multi-page TIFF of mixed sizes with CCITT for B&W, JPG/PNG/BMP, TIFF import via LibTiff, session
  round-trip, save flows, batch.
- 2026-10-28: Finding: Tesseract 5 `--psm 3` silently reads sideways text, and upside-down text OCRs into
  letter-like garbage. Scanner's raw text-length heuristic would fail here. The fallback uses `--psm 6` and only
  counts words with confidence ≥60 (upright 1681 vs best wrong angle 385).

- 2026-10-28: `NAPS2.Sdk` 1.6.0 + ImageSharp context on RHEL 9.7: SANE `test:0`
  discovered (flatbed+feeder, dpi 1–1200), 1 page scanned at 100 dpi, PDF exported.
  `ImageContext.TiffWriter` throws `NotSupportedException` on ImageSharp → own TIFF writer.
