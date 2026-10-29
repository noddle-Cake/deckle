# macOS verification (tasks.md 9.5)

Run `scripts/mac-check.sh` first. It runs the tests, builds `Scanwise.app`, runs the app's self-check and
screenshots the window. Then open the app (`open publish/osx-*/Scanwise.app`) and go through this list. Do it on
**macOS 26** (Liquid Glass) and **macOS 15**. Write anything that fails into the verification log in `tasks.md`.

## Window and sidebar
- [ ] The toolbar is in the title bar, and the close/minimize/zoom buttons don't overlap the sidebar button.
- [ ] Drag the window by empty toolbar space; double-click it to zoom.
- [ ] The sidebar shows the desktop blurred behind it. On macOS 26 it is a rounded glass panel, inset from the
      edges, with the traffic lights on it.
- [ ] View ▸ Hide Sidebar (⌃⌘S) hides it (and the glass panel); Show Sidebar brings it back.
- [ ] Narrow the window: the Pages | Editor switch appears, the centre buttons go, nothing overlaps at the
      minimum width.
- [ ] The green button: full screen and tiling both work; the layout follows.
- [ ] Quit and reopen: the window comes back at the same place and size.

## Appearance and accessibility
- [ ] Light and dark (System Settings ▸ Appearance), switching while the app runs.
- [ ] Increase Contrast: borders get stronger, grey captions turn dark.
- [ ] Reduce Transparency: the sidebar turns solid; nothing becomes see-through.
- [ ] VoiceOver (⌘F5): every toolbar and page-list button is read by name (not "button"); page thumbnails read
      as "Page 1", "Page 2"…
- [ ] Text uses the system font (San Francisco), the same as the menu bar.

## Menus and keyboard
- [ ] Menus: Scanwise, File, Edit, View, Page, Window, Help, in title case. No item shows a Ctrl shortcut.
- [ ] ⌘N scans, ⌘S saves, ⇧⌘S Save As, ⌘O imports, ⌘Z / ⇧⌘Z undo and redo, ⌘⌫ deletes the selected pages,
      ⌘, opens Settings, ⌘1–⌘3 switch panes.
- [ ] Each shortcut runs its action **once** (the window and the menu bar both know it; only one may act).
- [ ] In a text field (Settings ▸ File names, custom pattern): ⌘C, ⌘V, ⌘A, ⌘Z edit the text, not the pages.
- [ ] View ▸ Enter Full Screen appears once (AppKit may add its own).
- [ ] Dock icon menu: Scan, Scan into New Document; both work with the window in the background.
- [ ] Settings ▸ General ▸ Scan shortcut (⌃⌥ + letter) scans while another app is in front.

## Dialogs
- [ ] Profile ▸ Delete profile: Cancel on the left, Delete on the right. Return deletes; Esc and ⌘. cancel.
- [ ] Scan ▸ Scan and Insert Between Pages: the same order with three buttons in the area dialog (Preview).

## Documents, sharing, progress
- [ ] With unsaved pages the close button shows a dot; after saving, the title is the file name and has the
      file's proxy icon (⌘-click it shows the folder path).
- [ ] Copy a page (⌘C) and paste into Preview (File ▸ New from Clipboard) and into Pages or Notes.
- [ ] Share in the toolbar: the system share menu opens under the button, with AirDrop, Mail, Messages.
- [ ] A batch scan shows progress under the title and a percentage on the Dock icon; Esc or Stop cancels.
- [ ] Quit with pages in the list and reopen: they come back without a question, and the status bar offers
      Clear Pages….

## Scanning
- [ ] A network scanner (eSCL/AirScan) is found; macOS asks once for local network access.
- [ ] A USB scanner works through the bundled SANE.
