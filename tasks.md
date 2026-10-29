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
- [x] App bootstrap: composition root (`AppServices`, no DI container needed), FluentAvaloniaTheme, theme from settings (system/light/dark), single-window
- [x] Shell: left-compact NavigationView: Scan · Pages (badge = page count) · Editor; footer: Open save folder, Help, Settings. Adaptive layout: ≥1100px 3 panes (options | pages | editor), ≥775px 2 panes, narrow = one pane with top nav (Scanner's breakpoints).
- [x] Status InfoBar + progress ("Scanning page 2…", "Auto-rotating…", "Saving PDF…") with cancel
- [x] Error dialog with friendly device-error text + "Get help" link

### Phase 4: Scan options pane
- [x] Profile dropdown (Save as new / Rename / Delete)
- [x] Scanner dropdown with live discovery, refresh, driver badge (SANE/ESCL/WIA/TWAIN), "no scanner found" help
- [x] Source radio buttons (only supported ones enabled), feeder: duplex
- [x] Format: PDF / JPG / PNG / TIF / BMP
- [x] Color: Color / Grayscale / Black & white (only supported)
- [x] Resolution dropdown from caps with "recommended" hint (300 for text)
- [x] Page size: Auto/A4/Letter/Legal/A5/A3/B5/B4/Custom
- [x] Expander "More options": brightness/contrast sliders with reset, auto-deskew, auto-rotate, skip blank pages, OCR (searchable PDF) + language
- [x] Split Scan button: Scan · Add to current document · Start fresh · Merge into existing pages (Scanner's scan merge dialog: insert position, reverse order) · Batch scan…

### Phase 5: Pages + editor
- [x] Page list: thumbnail grid, multi-select (ctrl/shift), drag-drop reorder, drop files to import, context menu (rotate, delete, duplicate, move, export selected, open in default app); toolbar: select all, rotate, delete, reverse, interleave/deinterleave, import, save as
- [x] Undo/redo (Ctrl+Z/Ctrl+Y) wired to PageDocument
- [x] Editor: large preview with zoom/pan, prev/next, rotate 90/180/270, crop with handles + aspect presets (Scanner's list, with flip) + "apply to similar pages", adjustments panel (brightness, contrast, hue, saturation, sharpen, grayscale, B&W threshold) with live preview, deskew, delete, save
- [x] Draw/highlight (pen, highlighter, colors, thickness, undo stroke) burned into the image; undoable via the document

### Phase 6: Settings + saving flow
- [x] Settings view (Scanner layout, grouped cards): Save location (folder / ask every time + browse + open), File naming (presets + custom chip editor with live preview), Auto-rotate (toggle + OCR language), After scan (auto-save, add vs start fresh), PDF (OCR, PDF/A, metadata, password), Image quality (JPEG quality, TIFF compression), Appearance (theme), About/licenses
- [x] Save flow: auto-save after scan per settings; "Save as…" (file picker with format filter); success InfoBar with "Open file"/"Show in folder"
- [x] Batch scan dialog
- [x] Help view: short topics (scanner not found, choosing resolution, formats, scan merge, OCR languages)

### Phase 7: Polish
- [x] Keyboard shortcuts (Ctrl+N scan, Ctrl+S save, Ctrl+O import, Del, Ctrl+A, Ctrl+Z/Y, R rotate)
- [x] App icon, window title with page count
- [x] Session recovery prompt on start
- [~] Apple ICA driver (`net8.0-macos` TFM) — needs a Mac to build/test

### Phase 8: Ship
- [x] Headless UI smoke test (Avalonia.Headless): shell opens, navigation works, scan via SANE test device adds pages, save writes a file; render screenshots to verify layout
- [x] `scripts/publish.sh`: self-contained single-folder builds for linux-x64, linux-arm64, win-x64, osx-x64, osx-arm64
- [x] README: features, install, build, OS notes (SANE packages, Tesseract languages), credits to Scanner and NAPS2
- [x] Final review pass (`/code-review`-style self review), all tests green, commit

### Phase 9: macOS Human Interface Guidelines

Audit of 2026-10-29 against https://developer.apple.com/design/human-interface-guidelines/ (Menu bar,
Keyboards, Toolbars, Sidebars, Materials/Liquid Glass, Alerts, App icons, Accessibility). The UI is a
Windows 11 Fluent shell on every OS (FluentAvalonia `NavigationView`, `ContentDialog`, `SettingsExpander`).
That's the right call on Windows (see Decisions) and acceptable on Linux, but on macOS it breaks several HIG
rules. Almost every fix is a Mac-only branch, so Windows and Linux stay as they are. Items are ordered by
impact. Nothing here has been checked on a real Mac.

**What Avalonia 11.2 can and can't do.** Real Liquid Glass (macOS 26) comes from AppKit: `NSToolbar`,
`NSSplitViewController` sidebars, `NSGlassEffectView`. Avalonia draws its own controls, so it doesn't get
Liquid Glass. The realistic goal is a Mac-native *structure*: menu bar, ⌘ shortcuts, unified title bar,
translucent sidebar, Mac-ordered alerts. Do not paint imitation glass. The Materials page warns against glass
in the content layer ("can result in unnecessary complexity and a confusing visual hierarchy") and against
overusing it in custom controls.

#### 9.1 Must fix
Status 2026-10-29: implemented and unit-tested on Linux with `AppShortcuts.IsMac` switched on
(`MacConventionTests`); **not yet run on a Mac** (see 9.5). Code: `Services/AppShortcuts.cs`,
`Services/MacMenuBar.cs`, `Views/SettingsWindow.axaml`, `MacStyles.axaml`, `packaging/macos/`.

- [x] **⌘ instead of Ctrl on macOS.** Done: every shortcut is defined once in `AppShortcuts` and bound in
  code; the editor zooms with ⌘+scroll and trackpad pinch on Mac. Every gesture is hard-coded `Ctrl+…` (`MainWindow.axaml` KeyBindings,
  `PageListView.axaml`, `ScanOptionsView.axaml`, Ctrl+wheel in `EditorView.axaml.cs`). Keyboards page:
  "Prefer the Command key as the main modifier" and "Avoid using the Control key as a modifier". On a Mac
  the shortcuts are effectively missing.
  *Fix:* create the bindings in code from `PlatformSettings.HotkeyConfiguration.CommandModifiers` (Meta on
  macOS, Control elsewhere) rather than literal strings. On Mac: redo ⇧⌘Z (no Ctrl+Y), delete page ⌘⌫
  (keep Delete too), move page ⌥⌘↑/⌥⌘↓ instead of Alt+↑/↓, zoom with ⌘+wheel and trackpad pinch. Add a
  test that the macOS gestures use `KeyModifiers.Meta`.
- [x] **Shortcut text says "Ctrl".** Done: `{svc:ShortcutTip}`, `{svc:Shortcut}` and `{svc:ShortcutLabel}`
  markup extensions; Share shows no shortcut on Mac (⌘E is Find). Tooltips, `InputGesture=` on menu items, "Press Ctrl+Z to undo"
  (`MainViewModel.cs:550`), the shortcut table in `HelpView.axaml`, "Save (Ctrl+S)" in `SettingsView.axaml`,
  and the drawing caption in `EditorView.axaml`.
  *Fix:* one helper that renders `⌘S`, `⇧⌘S`, `⌥⌘↑` on Mac (modifier order ⌃⌥⇧⌘, as the HIG requires) and
  `Ctrl+S` elsewhere. Use it everywhere a shortcut appears in text.
- [x] **No menu bar.** Done as proposed below, except Show/Hide Sidebar (comes with the sidebar in 9.2) and
  Share as a plain item, not a submenu. Edit items act on a focused text field first, so ⌘C/⌘Z/⌘A still edit
  text. **Check on a Mac:** that a menu shortcut and the window's key binding don't both fire, and that AppKit
  doesn't add a second "Enter Full Screen". On macOS the app has only Avalonia's default app menu. Menu bar page: support App ·
  File · Edit · View · (app menus) · Window · Help in that order, and "Even when commands are available
  elsewhere in your app, it's important to list them in the menu bar". Toolbars page: "Make every toolbar
  item available as a command in the menu bar". Full Keyboard Access and VoiceOver users depend on it.
  *Fix:* on macOS only, set `NativeMenu.Menu` on the `Application` and on `MainWindow`, bound to the existing
  commands so each item's enabled state follows `CanExecute`. Disable items that can't run; don't hide them
  ("Always show the same set of menu items"). Proposed menus:
  - **Scanwise:** About Scanwise · Settings… ⌘, · Services · Hide / Hide Others / Show All · Quit ⌘Q
  - **File:** Scan ⌘N · Scan into New Document ⇧⌘N · Scan and Insert Between Pages… · Batch Scan… ·
    Preview · — · Import… ⌘O · — · Save ⌘S · Save As… ⇧⌘S · Save Selected Pages As… · Open in Another
    App · Share ▸ · Show Save Folder in Finder · — · Close Window ⌘W
  - **Edit:** Undo ⌘Z · Redo ⇧⌘Z · — · Copy ⌘C · Delete ⌘⌫ · Duplicate ⌘D · Select All ⌘A · Clear All
    Pages…
  - **Page:** Rotate Left ⌘L · Rotate Right ⌘R · Rotate 180° · Move Up ⌥⌘↑ · Move Down ⌥⌘↓ · Crop ·
    Adjust · Draw · Undo Edits on Page · Page Order ▸ (Reverse, Interleave…, Deinterleave…)
  - **View:** Scan / Pages / Editor ⌘1–⌘3 · Show/Hide Sidebar ⌃⌘S · Zoom In ⌘+ · Zoom Out ⌘− · Fit ⌘0 ·
    Previous/Next Page · Enter Full Screen ⌃⌘F
  - **Window:** Minimize ⌘M · Zoom · Bring All to Front
  - **Help:** Scanwise Help (opens the Help view)
- [x] **Settings is a page inside the main window.** Done: `SettingsWindow`, one at a time, closes with the
  main window. It stays resizable until 9.2 splits the long page into tabs. On macOS it belongs in its own window, opened with
  Scanwise ▸ Settings… or ⌘, (Menu bar and Keyboards pages). Today it also replaces the scan workspace while
  it's open.
  *Fix (Mac):* host `SettingsView` in a single non-resizable `Window` titled "Settings" (a second ⌘, brings it
  to the front). Remove Settings from the nav rail on Mac.
- [x] **Alert buttons are in Windows order.** Done differently from the fix below: `MacStyles.axaml` (loaded on
  Mac only) mirrors `ContentDialog`'s button grid, so every existing dialog gets Cancel · Secondary · Default
  without a second dialog type. ⌘. cancels. No destructive style: "Clear all pages" is a deliberate,
  undoable choice, and the HIG keeps that style for actions people didn't choose. The confirm buttons already
  use specific verbs (Replace, Delete, Clear pages, Restore). FluentAvalonia's `ContentDialog` puts the primary button on the
  left and Close/Cancel on the right. Alerts page: "Always place the default button on the trailing side of
  a row … Cancel buttons are typically on the leading side". This affects every dialog in `UiServices`
  (confirm, text prompt, merge, scan area, batch). Esc and ⌘. must cancel, and Return must choose the default.
  *Fix (Mac):* a small dialog window shown with `ShowDialog(owner)` and laid out Cancel · secondary ·
  default, from leading to trailing. Use the destructive style for "Clear all pages". Keep `ContentDialog`
  on Windows.
- [x] **Ship a real `.app` bundle.** Done: `publish.sh` builds `Scanwise.app` (bundle ID
  `com.cjservin.scanwise`, plus `NSLocalNetworkUsageDescription` and the eSCL Bonjour types so macOS 15+
  allows network scanner discovery) and zips it with `zip -y`, since `ditto` isn't available on Linux.
  Signing and notarization still wait for a Developer ID. `scripts/publish.sh` makes a bare folder and a tar.gz for `osx-*`. With
  no `Scanwise.app` and no `Info.plist`, the Dock shows no proper name or icon, Finder treats the binary as a
  Unix executable, and macOS has no bundle ID for permissions or the share sheet.
  *Fix:* build `Scanwise.app/Contents/{MacOS,Resources,Info.plist}` with `CFBundleIdentifier`,
  `CFBundleName`, `CFBundleIconFile`, `LSApplicationCategoryType=public.app-category.productivity`,
  `NSHighResolutionCapable` and `LSMinimumSystemVersion`. Archive it with `ditto -c -k --keepParent` (not
  tar) and sign/notarize once a Developer ID is available.
- [~] **No macOS app icon.** Partly done: `packaging/macos/icon.svg` redraws the icon on the 1024 px grid
  (824 px body, shadow) and `scripts/make-icns.py` builds `Scanwise.icns` (16–1024 at @1x/@2x). Still to do:
  the layered Icon Composer `.icon` for macOS 26, which needs a Mac. There's only `scanwise.ico`/`.png`. App icons page: use Apple's
  rounded-rectangle grid and let the system apply the shape. On macOS 26 the icon is layered (Icon Composer
  `.icon`), so the system can render it in Liquid Glass and in the dark, clear and tinted appearances.
  *Fix:* separate foreground/background layers on the 1024 px template, exported with Icon Composer, plus an
  `.icns` (16–1024 at @1x/@2x) for macOS 14–15.

#### 9.2 Should fix: layout and structure

Status 2026-10-29: done except where noted, tested on Linux with `AppShortcuts.IsMac` on and checked in
headless screenshots (wide, sidebar hidden, narrow, Settings); **not yet run on a Mac**. Code:
`Views/MacShell.axaml`, `Views/MacSettingsView.axaml`, `Views/HelpWindow.axaml`, `MacIntegration.cs`.
On a Mac, check: blur behind the sidebar, the window dragging from the toolbar, traffic-light spacing, the
share menu's position, and that pasted images arrive in Preview/Pages.

- [x] **Unified title bar and window title.** Done: 52 px title bar holding the toolbar; title = file name,
  "Untitled" or "Scanwise" with no pages; `NSWindow.documentEdited` and `representedFilename` (proxy icon).
  The page count moved to the toolbar subtitle. The app uses a standard title bar over a Fluent nav rail, and
  the title is "Scanwise · 3 pages •". Toolbars page: "Don't title windows with your app name" and keep titles
  short. On macOS unsaved changes appear as the dot in the close button, not in the title.
  *Fix (Mac):* `ExtendClientAreaToDecorationsHint=true` with a ~78 px leading inset for the traffic lights.
  Title = document name ("Untitled" until saved, then the file name). Show the page count in the Pages
  header. Set `NSWindow.documentEdited` through the ObjC runtime (as `MacIntegration.cs` already does for the
  Dock menu) instead of appending "•".
- [x] **Sidebar instead of a Fluent compact nav rail.** Done: `MacShell` replaces the `NavigationView` on Mac.
  The scan options are a full-height sidebar, transparent over `AcrylicBlur`, falling back to a solid colour
  when blur is unavailable. View ▸ Show/Hide Sidebar ⌃⌘S. In a narrow window the sidebar button swaps sidebar
  and pages, and a Pages | Editor switch replaces the top navigation. Help opens in its own window, like
  Settings. `NavigationView PaneDisplayMode="LeftCompact"` is a
  Windows pattern. On Mac the scan options are the natural sidebar and pages/editor are the content and
  detail columns. Sidebars page: "consider using a split view interface that includes a content list between
  the sidebar items and detail view". Settings, Help and "Open save folder" sit at the bottom of the rail;
  the same page says "Avoid putting critical information or actions at the bottom of a sidebar". Once 9.1 is
  done they live in the menu bar.
  *Fix (Mac):* drop the nav rail and keep the three-column `Workspace` grid. Make the scan-options column a
  full-height sidebar under the traffic lights, with system vibrancy: `TransparencyLevelHint=AcrylicBlur` on
  the window and a transparent `PaneBackground` on Mac. Add View ▸ Show/Hide Sidebar. When the window narrows,
  collapse the sidebar instead of switching to top navigation, as the Sidebars page suggests. Don't hide it by
  default.
- [x] **Toolbar grouping.** Done as proposed. Scan, with its split menu, appears only in the toolbar. The pane
  buttons it replaces (rotate, copy, share, save as, undo/redo) are hidden on Mac. In a narrow window the
  centre group collapses, and its commands stay in the menu bar. The borderless icon buttons already match the HIG's "no bezel" style. But there
  are two toolbars with many groups and no single primary action. Toolbars page: "aim for a maximum of three"
  groups. Leading: sidebar toggle and title. Centre: common actions. Trailing: inspector, search, More.
  "Only specify one primary action, and put it on the trailing side".
  *Fix (Mac):* one toolbar in the unified title bar. Leading: sidebar toggle and document title. Centre:
  Rotate Left/Right, Crop, Adjust, Draw. Trailing: Share and **Scan**, which is the only prominent button.
  Duplicate, Move, Page order and Open in another app stay in More and in the menu bar. Drop the painted
  toolbar background ("Reduce the use of toolbar backgrounds and tinted controls").
- [x] **The editor's tool panel acts as an inspector.** Decided against a separate ⌥⌘I toggle. The panel is
  a tool mode (Crop/Adjust/Draw with Apply/Cancel), not a property inspector, and the toolbar's Crop/Adjust/Draw
  toggles already open and close it; a generic toggle could throw away an unfinished crop. It stays solid
  because it sits over the editor canvas, not the window background. Crop/Adjust/Draw open a 280 px right-hand panel.
  On Mac, toggle it from a trailing toolbar button and View ▸ Show Inspector ⌥⌘I, using the sidebar's
  material.
- [x] **Share opens Mail only.** Done: `NSSharingServicePicker` anchored to the clicked button, or the top
  centre when Share comes from the menu bar. `PlatformShell.ShareAsync` runs `open -a Mail`. Mac users expect the system
  share menu (AirDrop, Messages, Notes, Mail…).
  *Fix:* show `NSSharingServicePicker` via the ObjC runtime, anchored to the Share button, with the files as
  `NSURL`s. Drop the ⌘E binding on Mac: the Keyboards page reserves ⌘E for "Use the selection for a find
  operation".
- [x] **Copying runs AppleScript.** Done: PNG and TIFF on the general pasteboard. `CopyImageAsync` calls `osascript` with a script built from a string.
  That's slow and may raise an Automation permission prompt. Write PNG and TIFF to
  `NSPasteboard.generalPasteboard` directly.
- [x] **Settings controls.** Done: `MacSettingsView` in a fixed-size window, with the four tabs, right-aligned
  labels and checkboxes. About is in the app menu. `SettingsExpander` cards with `ToggleSwitch` look like Windows Settings. On Mac,
  use a grouped form in the Settings window, with labels on the leading side and controls on the trailing
  side, and checkboxes for most options. The HIG Toggles page reserves macOS switches for major on/off
  settings. Split the window into tabs: General, Saving, Text Recognition, PDF & Images.
- [x] **Global hotkey uses Control.** Won't do: ⌃⌥ stays. A global hotkey takes its key combination away
  from every app, and ⌥⌘ combinations are taken widely (⌥⌘D Dock, ⌥⌘H Hide Others, ⌥⌘S Save All). ⌃⌥ is
  the usual choice for system-wide shortcuts; the HIG's "avoid Control" is about shortcuts inside an app.
  Original note: `MacIntegration.RegisterHotkey` registers ⌃⌥ + key. Global shortcuts
  are a special case, but Command is still preferred: default to ⌥⌘ + key, keep ⌃⌥ selectable, and render
  the label as `⌥⌘S`.

#### 9.3 Polish

Status 2026-10-29: done except the icon set (needs a decision) and the parts that need a Mac to check.
Tested on Linux with `AppShortcuts.IsMac` on (`MacConventionTests`, 24 tests).

- [x] **System font.** Done: no `WithInterFont()` on macOS, so text uses San Francisco. `ScanMergeDialog`'s
  `Consolas,Menlo,monospace` already lands on Menlo (SF Mono isn't installed as a normal font), so it's
  unchanged.
- [~] **Hard-coded Windows colours.** Partly done: Increase Contrast (and Windows contrast themes) now
  strengthens card/pane borders and shows captions at full opacity (`App.FollowContrastSetting`, via
  Avalonia's `ContrastPreference`). The sidebar takes its colour from the system vibrancy (9.2). Still to do:
  read `NSColor` window and separator colours for the content area, and adjust toolbar corner radii, which
  can only be judged on a Mac.
- [x] **Sidebar icons use the accent colour.** Not applicable: the Mac sidebar is the scan options form, with
  no icon list. Checkboxes and radio buttons already follow the accent colour.
- [ ] **Icons look like Windows.** **Needs a decision:** which icon set to use (licence and look); see the
  report. Original note: The Fluent symbols read as Windows. SF Symbols may be used only in apps for
  Apple platforms, which a cross-platform build isn't. Use a neutral outline icon set that doesn't read as
  Windows-specific.
- [x] **VoiceOver labels.** Done on every platform (helps Narrator and Orca too): a style names every icon
  button after its tooltip, without the shortcut; page thumbnails ("Page 3") and colour swatches are named
  too. A test checks that every visible icon button has a name.
- [x] **Reduce Motion / Reduce Transparency.** Nothing to change: the app has no animations of its own (only
  FluentAvalonia's short dialog fade), and with Reduce Transparency macOS makes the vibrancy behind the
  sidebar opaque by itself. The window also falls back to a solid background when blur isn't available.
- [x] **Alert at launch.** Done on macOS: pages are restored without asking, and the status bar says so with a
  "Clear Pages…" button. Windows and Linux keep the question.
- [x] **Progress.** Done: on Mac the toolbar subtitle shows the busy text, a small progress bar and Stop
  (Esc), and the Dock icon shows a percentage badge. The bar above the content is hidden on Mac.
- [~] **Window behaviour.** Done: the window frame (position, size, maximized) is saved on close and restored
  on start, on every OS, and ignored if it's off every connected screen. The Mac minimum width is now 560 so
  the toolbar never overlaps; at minimum width the toolbar's sidebar button always shows. Still to check on a
  Mac: full screen and tiling from the green button.
- [x] **Title case.** Done: on macOS every menu item header is converted to title case as it's set
  (`TitleCase`, Apple's rule for minor words); buttons keep sentence case. The Dock menu now has "Scan" and
  "Scan into New Document".

#### 9.4 Liquid Glass (macOS 26), optional, after 9.1–9.2
- [ ] Spike: put a native `NSGlassEffectView` behind the sidebar region of Avalonia's `NSWindow` (fall back
  to `NSVisualEffectView` with the `.sidebar` material on macOS 15 and earlier), through the ObjC runtime as
  in `MacIntegration.cs`, with Avalonia's surface transparent over it. Only the sidebar and the
  title/toolbar band get glass. The page canvas, thumbnails and settings stay in the content layer
  (Materials: "Don't use Liquid Glass in the content layer"). Use the *regular* variant: the sidebar is
  text-heavy, and the clear variant is meant for media backgrounds. If it proves brittle, stop at the
  `AcrylicBlur` vibrancy from 9.2.
- [ ] Long term: a native AppKit shell (the `net*-macos` target already planned for Apple ICA) would give
  real `NSToolbar`, sidebars and Liquid Glass. Revisit when the ICA task starts.

#### 9.5 Verification (needs a Mac)
- [ ] Run on macOS 15 and 26, in light and dark, with Increase Contrast, Reduce Transparency and VoiceOver.
- [ ] Every menu item works and shows its ⌘ shortcut; no Ctrl shortcut remains on Mac.
- [ ] Alerts: Return chooses the default (trailing) button, Esc and ⌘. cancel (leading).
- [ ] Unit-test the shortcut/label helpers and the menu construction on Linux with a faked macOS platform.

## Verification log

- 2026-10-29: Phase 9.1 (macOS HIG). App tests 19/19 (8 new in `MacConventionTests`: ⌘ gestures and labels,
  window bindings, Settings window, menu bar contents and order, Edit menu on a text field, dialog button
  order with and without `MacStyles.axaml`); core 47 passed, 4 skipped (OSD/GNOME env not set). `publish.sh
  osx-arm64` produced `Scanwise.app` (Info.plist, PkgInfo, 10-size `.icns`, executable bit kept in the zip).
  Not run on a Mac.

- 2026-10-28: Full suite 57/57 with no skips (51 core + 6 UI). `scripts/publish.sh` produces Release
  self-contained builds for linux-x64, linux-arm64, win-x64 (includes the NAPS2 TWAIN worker), osx-x64 and
  osx-arm64 (bundled libsane, Tesseract, PDFium). The linux-x64 binary starts and reaches X11 init. It wasn't
  run interactively on Windows or macOS (no machines here).
- 2026-10-28: Bug found and fixed: a two-way `!IsHighlighter` binding on a radio group caused an infinite
  binding loop (caught as a hung UI test, diagnosed with dotnet-stack).

- 2026-10-28: Headless UI tests (Avalonia.Headless + Skia, real rendering) 6/6: layout breakpoints, SANE scan →
  auto-save PDF, append to the same PDF, crop, adjust, save, feeder → one JPG per page, delete + undo of a
  multi-selection, session restore after restart, and the merge/batch/region dialogs. I reviewed the screenshots
  (`SCANWISE_SCREENSHOTS=dir scripts/test.sh`).
- 2026-10-28: Bug found by the UI test and fixed: binding ListBox.SelectedItem collapsed a multi-selection to one
  page, so Delete removed one page instead of the whole selection. The editor's page is now derived from
  selection changes instead.

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
