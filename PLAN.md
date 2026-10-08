# PowerPen — Build Plan

**Status:** v0.11 — 2026-10-07. Phases 0–4 done and manually tested. Phase 5 built and verified by automated tests. Phase 6 in progress.

---

## 1. Goal

A personal Epic Pen Pro–style screen annotation tool for Windows. Draw on top of everything, on any monitor, then screenshot the result. Shared with a few teammates. Not a product.

**Guiding rules**
1. The toolbar is dead simple: one click per action, and every tooltip shows its hotkey.
2. Use WPF's built-in `InkCanvas`. Don't write a drawing engine.
3. Build and test the risky parts first: multi-monitor scaling, click-through, screen capture.
4. No installer, no accounts, no network access. One `.exe`.

---

## 2. Scope

| Feature | Epic Pen Pro | PowerPen v1 | Later (v2) |
|---|:-:|:-:|:-:|
| Pen (with pen pressure) | ✅ | ✅ | |
| Highlighter | ✅ | ✅ | |
| Eraser (whole stroke) + Clear all | ✅ | ✅ | |
| Undo / Redo | ✅ | ✅ | |
| Colors with hotkeys | 16–24 | 8 (customizable) | |
| 3 thickness presets | ✅ | ✅ | |
| Shapes: line, arrow, rectangle, ellipse | ✅ | ✅ | Filled shapes |
| Text tool | ✅ | ✅ | |
| Screenshot (region or full monitor) → Downloads + clipboard | ✅ | ✅ toolbar button only, no hotkey | Region across monitors |
| Customizable hotkeys | ✅ | ✅ | |
| Pointer mode (ink stays, clicks pass through) | ✅ | ✅ | |
| Works on all monitors with mixed scaling | ✅ | ✅ | |
| Whiteboard / blackboard background | ✅ | | ✅ (cheap) |
| Fading ink | ✅ | | ✅ |
| Cursor highlight halo | ✅ | | ✅ |
| Select / move strokes | | | ✅ (InkCanvas has it built in) |

---

## 3. Build vs. reuse

**Decision: build from scratch on WPF.**

| Option | Verdict |
|---|---|
| **ppInk** (MIT, C#, free) | Already does nearly all of this. Rejected as a base because it uses WinForms and the legacy `Microsoft.Ink.dll`, which broke in a Windows 24H2 update. The fix needs an admin script, which is a problem on corporate laptops. The codebase is also large and the UI is busy. |
| **ZoomIt** (Microsoft, free, signed) | Keyboard-only, with no toolbar. Fine for power users but not "super simple." |
| **Build on WPF** | `InkCanvas` gives us pen, highlighter, pressure, eraser, and smoothing for free. Our real work is the overlay windows, hotkeys, shapes, text, and capture. |

---

## 4. Tech stack

| Choice | Why |
|---|---|
| **C# / WPF / .NET 10** | LTS until Nov 2028. SDK 10.0.401 is already installed. `InkCanvas` is built in. |
| **CommunityToolkit.Mvvm** | Cuts out property-changed boilerplate in the toolbar and settings. |
| **`UseWindowsForms=true`** | Only for the tray icon (`NotifyIcon`) and `System.Drawing` screen capture. ⚠ Some type names exist in both WPF and WinForms (`Application`, `Color`, `Point`), so we use aliases. |
| **Win32 P/Invoke (one file)** | Global hotkeys, click-through, exact window placement, hiding the toolbar from capture, finding the Downloads folder. |
| **System.Text.Json** | Settings file. |
| **Nothing else** | Fewer dependencies means fewer things to break. |

---

## 5. How it works

### 5.1 Two modes

| Mode | Ink | Mouse |
|---|---|---|
| **Pointer** (default) | Visible | Passes through to the apps underneath |
| **Draw** | Visible | Captured by PowerPen and uses the current tool |

- `Esc` or the toggle hotkey switches back to Pointer mode, and focus returns to the app you were in (e.g., a PowerPoint slideshow).
- **Hide ink** is a separate toggle. It hides the ink without deleting it.

### 5.2 One overlay window per monitor

- **Why not one giant window?** A WPF window can only have one scaling (DPI) value. With a laptop at 150% and a monitor at 100%, one of them would be scaled wrong. Each per-monitor window gets its own DPI, and smaller transparent windows also render faster.
- **Tradeoff:** a single stroke can't cross from one monitor to the next. That's acceptable.
- **Note on "multiple resolutions":** the real problem is different **scaling %**, not resolution. Resolution alone is easy.
- **Your setup:** 3 monitors, all at 100%:
  - 2560×1440 (main)
  - 1920×1080 to the right
  - 1080×1920 **portrait** to the left, at negative coordinates
  This never exercises mixed scaling, but teammates' laptops (usually 125–150%) will. Verified by hand with one monitor at 150% (2026-10-07).

**Recipe**
1. Make the app Per-Monitor V2 DPI aware in `app.manifest`.
2. Place each overlay with `SetWindowPos` using that monitor's **physical pixel** bounds, and place it again after any `DpiChanged` event.
3. When a monitor is plugged in, unplugged, or changes resolution, `SystemEvents.DisplaySettingsChanged` fires and we rebuild the overlays. In v1, ink on screen is cleared when this happens.

### 5.3 Overlay window setup

- Window settings: `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`, `ShowInTaskbar=False`, plus `WS_EX_TOOLWINDOW` so it doesn't appear in Alt+Tab.
- **Draw mode:** the InkCanvas background is `#01000000`. Alpha 1 is invisible but still catches clicks.
- **Pointer mode:** the background is fully transparent and `WS_EX_TRANSPARENT` is added, so every click falls through.
- ⚠ **Never exclude the overlays from screen capture.** The ink must show up in screenshots and in Teams/Zoom screen shares.

### 5.4 Tools → InkCanvas

| Tool | How |
|---|---|
| Pen | `EditingMode=Ink`, `FitToCurve=true`, pressure on |
| Highlighter | Ink + `IsHighlighter=true`, rectangular tip, 3× width |
| Eraser | `EraseByStroke` removes whole strokes, like Epic Pen. Text is removed when the eraser passes within 8 units of it, checked by position. |
| Line / Arrow / Rectangle / Ellipse | `EditingMode=None`. We handle mouse down/move/up ourselves and show a live preview in a separate `InkPresenter`, so dragging doesn't flood the undo history. On release, the shape becomes **one** normal `Stroke`. **Shift** constrains it (45° lines, squares, circles) and updates the preview instantly when pressed or released. `Esc` mid-drag cancels the shape. |
| Text | Click to place a `TextBox`. `Enter` or clicking elsewhere finishes it. `Shift+Enter` adds a new line, `Esc` cancels. The finished item is the same box made read-only and borderless, so the text doesn't jump on finish. Items ignore the mouse, so the pen can draw over text. |

- **Why shapes become strokes:** eraser, undo, clear, and screenshots then work on shapes with zero special cases.
- **One stroke per shape**, including the arrow (tail → tip → wing → tip → wing), so each shape erases and undoes as a unit.
- **Shape strokes turn off curve smoothing**, so rectangle corners and arrowheads stay sharp.
- **Text size follows the size button:** 18 / 28 / 40.
- **Color changes apply to the box you're typing in.**
- **Shortcuts are paused while a text box is open**, so typing "hello" doesn't switch to the highlighter.

### 5.5 Undo / redo

- One app-wide stack that covers all monitors, so `Ctrl+Z` undoes the last thing you did no matter where it was.
- Actions: `AddStrokes`, `RemoveStrokes`, `AddText`, `RemoveText`, `ClearAll` (keeps a snapshot).
- Recorded from `StrokeCollection.StrokesChanged` and text added/removed events. A flag pauses recording while an undo or redo is running.
- **Clear is undoable**, so an accidental clear isn't fatal.

### 5.6 Hotkeys

There are two layers. Global single-letter hotkeys would break typing in every other app, so plain-letter keys only work in Draw mode.

**Global** (`RegisterHotKey`, work from any app)

| Action | Default |
|---|---|
| Toggle Draw / Pointer mode | `Ctrl+Alt+D` |
| Pen in color 1–8 (also enters Draw mode) | `Ctrl+Alt+1`…`8` |
| Clear all ink | `Ctrl+Alt+C` |
| Hide / show ink (also on the toolbar: 👁) | `Ctrl+Alt+I` |
| Quit | `Ctrl+Alt+Q` |

- All global hotkeys are set in `settings.json` (§5.9). An empty value turns a hotkey off.
- Validation catches invalid combos, duplicates, and collisions with the color hotkeys, and explains each one.
- ⚠ **F12 is rejected.** Microsoft's docs reserve it for debuggers: registering it succeeds, but it may never fire. The Phase 2 self-test hit exactly this.
- ⚠ **Taken on the company image** (probed 2026-10-07): `Ctrl+Alt+H` and `Ctrl+Alt+M` are owned by another app. That's why hide/show ink is `Ctrl+Alt+I` ("I" for ink). Every `Ctrl+Alt+Shift+letter/digit` was free.
- ⚠ **Global hotkeys override app shortcuts.** While PowerPen runs, Word's `Ctrl+Alt+1/2/3` (Heading 1–3) and `Ctrl+Alt+C` (©) go to PowerPen instead. If that matters to you, set `colorModifiers` to `Ctrl+Alt+Shift`.
  - Decision (2026-10-07): keep `Ctrl+Alt+1–8`.
- ⚠ **Non-US keyboards:** on many European layouts `Ctrl+Alt+digit` is AltGr, which types characters like `@`, `{`, `[`. If a teammate uses one, change `colorModifiers` (e.g., to `Ctrl+Alt+Shift`) or set it to empty.

**Draw mode only** (plain keys; ignored while typing in a text box)

| Key | Action | Key | Action |
|---|---|---|---|
| `P` | Pen | `1`–`8` | Color |
| `H` | Highlighter | `[` / `]` | Thinner / thicker |
| `E` | Eraser | `Ctrl+Z` / `Ctrl+Y` | Undo / redo |
| `L` | Line | `Delete` | Clear all |
| `A` | Arrow | `Esc` | Cancel current text/shape, otherwise back to Pointer mode |
| `R` | Rectangle | `Shift` (while dragging) | Constrain shape |
| `O` | Ellipse | `T` | Text |

- Draw-mode keys are fixed, not configurable. They only work while a PowerPen overlay has focus, so they can't clash with other apps.
- `-` / `=` also work for thinner/thicker.
- Shift is ignored for single-key shortcuts.

**If a hotkey is already taken** by another app (or by a second running copy of PowerPen), PowerPen keeps running. All startup problems are shown together in one message, for example:
> "Ctrl+Alt+D (switch between Draw and Pointer mode) is already used by another app or another copy of PowerPen. To fix: close the other app, or change "hotkeys.toggleDraw" in the settings file, then restart PowerPen."

Phase 5 replaces "edit the settings file" with a Settings window.

### 5.7 Screenshots

**Flow:** one toolbar button, no hotkey.
1. Clicking 📷 immediately captures **every** monitor. This freezes the image, so what you see is what you get.
2. Each monitor shows its frozen image slightly dimmed, with the hint "Drag to capture an area · Click to capture this screen · Esc to cancel."
   - **Drag** a rectangle → captures that area.
   - **Single click** → captures that whole monitor. This also lets you click 📷 on one monitor and capture a different one.
3. Crop, save as PNG, copy to the clipboard, and show a toast: "Saved PowerPen_2026-10-07_143205.png." Clicking the toast opens the folder with the file selected.

**Selection screen**
- The selected area shows undimmed, with a dashed border and its real pixel size (e.g. "512 × 288").
- **Cancel** with Esc, a right-click, or by switching apps (Alt+Tab or the Win key). The last one ensures frozen images can never be left covering the monitors.
- Works in both modes, and afterwards returns you to where you were: the overlay if you were drawing, otherwise your app.
- Any text being typed is finished first, so it's in the picture.

**Details**
- **Tooltip timing:** the screen is frozen 150 ms after the click. The toolbar is excluded from captures, but its tooltip is a separate window, and this gives it time to disappear.
- **Toast:** appears in the bottom-right of the monitor the shot was taken on and disappears after 5 s (hovering pauses it). Clicking it opens Explorer with the file selected. It never takes focus and never appears in captures.
- **Clipboard:** retries 3 times if another app is holding the clipboard. If it still fails, the toast says so and the file is still saved.
- **Save errors** (folder missing, no permission) show a message that names the folder, says whether the picture is still on the clipboard, and explains how to fix it.
- **No overwrites:** two shots in the same second get `_2`, `_3`, and so on, and files are opened with "create new" so an existing file can never be overwritten.
- **Capture method:** GDI `BitBlt` with `SRCCOPY | CAPTUREBLT`, called directly. `CAPTUREBLT` is what includes the transparent ink overlay. It captures physical pixels, so you get full native resolution on every monitor.
  - ⚠ `Graphics.CopyFromScreen` can't be used: it throws if `CaptureBlt` is combined with any other flag. The Phase 0 self-test found this.
- **Toolbar never appears in screenshots:** it's set with `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, so there's no hide-and-flash trick. Side effect: the toolbar is also hidden from Teams/Zoom shares. This is a setting.
- **Downloads folder:** found with `SHGetKnownFolderPath(FOLDERID_Downloads)`, **not** `%USERPROFILE%\Downloads`, because corporate OneDrive can redirect it. The folder can be overridden in Settings.
- **v1 limit:** a region must be on a single monitor.

### 5.8 Toolbar UI

Two columns, every tool one click away (about 94 × 390 at 100%):

```
┌─────────────┐
│     ══      │  drag handle
│  ↖     ✎    │  Pointer · Pen
│  ▮     ⌫    │  Highlighter · Eraser
│  ╱     ↗    │  Line · Arrow
│  ▭     ⬭    │  Rectangle · Ellipse
│  A     ●    │  Text · Size (dot shows size + color; click cycles S/M/L)
├─────────────┤
│  ● ● ● ●    │  8 color swatches (active one has a ring)
│  ● ● ● ●    │
├─────────────┤
│  ↶     👁    │  Undo · Hide/show ink (lit up while hidden)
│  🗑     📷   │  Clear all · Screenshot (Phase 4)
│  ⚙     ▴    │  Settings · Collapse (Phase 5)
└─────────────┘
```

- **Changed in Phase 3:** the plan originally put the four shapes behind one "Shapes ▸" flyout. Two columns were chosen instead:
  1. One click to any tool, instead of two plus a hidden menu.
  2. The toolbar is shorter (about 390 vs. 760 tall), so it fits on laptop screens.
  3. A flyout needs its own popup window, which brings back the focus and z-order problems the toolbar already solved.
- Shape icons are drawn as outlines rather than font glyphs, so they show exactly the shape they draw.
- `PowerPen.exe --render-ui [folder]` renders the toolbar (normal, drawing, collapsed) and the Settings window to PNGs for layout checks. It draws only PowerPen's own UI and never captures the screen.
- Floating and draggable. Remembers its position, and snaps back to the primary monitor if that screen is gone.
- The active tool is highlighted. Tooltips show the hotkey, e.g., "Pen (P)", so people learn the keys by using the toolbar.
- Icons come from the **Segoe Fluent Icons** font, which is built into Windows 11, so there are no image files to manage.
- `WS_EX_NOACTIVATE`: clicking the toolbar doesn't steal focus from PowerPoint or other apps.
- Always sits above the overlays. Topmost is re-applied after every mode change.

- **Collapse (▴):** shrinks the toolbar to one pen button, which is lit while drawing. Click it to bring the toolbar back.
- **Position memory:** the toolbar's position and collapsed state are saved. If that spot is no longer on any monitor (unplugged or rearranged), it returns to the default spot, also while running.

**Tray icon menu:** Show toolbar · Settings… · Open screenshots folder · Open log folder · Exit PowerPen.

### 5.9 Settings

The settings file is `%AppData%\PowerPen\settings.json`. It's created with defaults on first run.

As of Phase 2:

```json
{
  "colors": [
    { "name": "Red", "color": "#E53935" },
    { "name": "Orange", "color": "#FB8C00" },
    "... 8 entries max; names appear in tooltips"
  ],
  "penSizes": { "small": 2, "medium": 4, "large": 8 },
  "highlighterWidthMultiplier": 3,
  "hotkeys": {
    "toggleDraw": "Ctrl+Alt+D",
    "toggleInkVisible": "Ctrl+Alt+I",
    "clearAll": "Ctrl+Alt+C",
    "quit": "Ctrl+Alt+Q",
    "colorModifiers": "Ctrl+Alt"
  }
}
```

**Later phases add their own fields only when they use them,** so the file never has settings that do nothing.

Added in Phase 4:
```json
  "screenshotFolder": "",               // empty = Downloads; full path; %USERPROFILE% etc. work
  "copyScreenshotToClipboard": true,
  "hideToolbarFromCapture": true        // also hides it from Teams/Zoom shares
```
An existing settings file doesn't list these until you add the lines; missing fields use the defaults above.

Added in Phase 5 (saved automatically when you move or collapse the toolbar):
```json
  "toolbar": { "x": 40, "y": 200, "collapsed": false }   // x/y in physical pixels; null = default spot
```

**Rules**
- The file is hand-editable: comments and trailing commas are allowed, and missing fields keep their defaults.
- **Invalid values** (bad color, out-of-range size, bad or duplicate hotkey) are replaced with defaults in memory and explained at startup. The file itself is left alone for the user to fix.
- **Unreadable file** (broken JSON): it's renamed to `settings.bad.json`, fresh defaults are written, and the user is told the line number.
- **Locked file:** defaults are used for this session and the file isn't touched.
- **Tradeoff:** every default is written into the file on first run, which makes the file easy to discover and edit. The cost is that changing a default later doesn't reach people who already have a file; they keep the old value. The `Ctrl+Alt+H` → `Ctrl+Alt+I` change hit this, and your file was regenerated. Settle the defaults before sharing in Phase 6.
- The file is written with `+` as-is. By default .NET escapes `+` into a six-character code, which made hotkeys unreadable.
- **Saved safely:** each save writes a temporary file and then swaps it in, so a crash mid-save can't corrupt settings.
- **Never overwrites unreadable settings:** if the file couldn't be read at startup, PowerPen won't save over it that session.
- **Edited in the app (Phase 5):** the ⚙ Settings window covers colors, sizes, hotkeys (with a recorder), the screenshot folder, the clipboard and capture-hiding options, and starting with Windows.
  - Changes apply right away, with no restart.
  - Saving from the window rewrites the file, so any hand-written comments in it are dropped.
- **"Start with Windows" isn't in this file.** It lives only in the per-user `Run` registry key, so it can never disagree with what Windows actually does. It also follows the exe: if the exe is moved, the box shows unticked until saved again.

### 5.10 Errors & logging

- **Logging:** daily log files in `%AppData%\PowerPen\logs\`, kept for 14 days. They record startup (version, Windows, each monitor), settings and hotkey problems, saves, and full error details. Logging never throws.
- **Unexpected errors** (UI thread, background threads, background tasks) are logged. UI-thread errors also show a friendly message with the log's path, and PowerPen keeps running.
- **Single instance:** a named mutex, scoped to the signed-in user's session. Launching PowerPen a second time signals the running copy to show its toolbar, then exits. This prevents duplicate overlays and hotkey conflicts.
- **Tray icon** (by the clock):
  - Left-click brings back the toolbar.
  - The right-click menu has Show toolbar · Settings… · Open screenshots folder · Open log folder · Exit PowerPen.

---

## 6. Architecture

```
  Toolbar clicks ─┐
  Global hotkeys ─┼──▶  AppCommands  ──▶  AppState  (mode, tool, color, thickness, ink visible)
  Draw-mode keys ─┤     (one place for         │  change events
  Tray menu ──────┘      every action)         ▼
                                         OverlayManager ◀── DisplaySettingsChanged → rebuild
                                          │      │      │
                                          ▼      ▼      ▼
                                      Overlay Overlay Overlay   (one per monitor, each with an InkCanvas)
                                          └──────┼──────┘
                                                 ▼
                                           UndoManager (shared)

  ScreenshotService: BitBlt monitor → (RegionSelectWindow) → crop → PNG → folder + clipboard → Toast
```

**Key design rule:** every action (toolbar, hotkey, tray) calls the **same** `AppCommands` method. That way there aren't three slightly different versions of "clear."

| Component | Job |
|---|---|
| `App` | Startup: single-instance check, load settings, create services and windows, global error handler |
| `AppState` | Single source of truth for mode, tool, color, thickness, and ink visibility. Observable. |
| `AppCommands` | Every user action lives here |
| `OverlayManager` | Lists monitors, creates and rebuilds one `OverlayWindow` per monitor, applies mode changes |
| `OverlayWindow` | Transparent full-monitor window. Hosts the InkCanvas and turns input into strokes, shapes, or text. |
| `ShapeBuilder` | Pure math: start, end, shape, and constrain → stylus points. Unit-tested. |
| `TextTool` | Creates, commits, and cancels text boxes |
| `UndoManager` | App-wide undo/redo stack |
| `HotkeyService` / `HotkeyParser` | Registers global hotkeys, parses strings like `"Ctrl+Alt+D"`, reports conflicts |
| `ScreenshotService` / `RegionSelectWindow` | Capture, select, crop, save, clipboard |
| `ToolbarWindow` / `ToolbarViewModel` | Main UI |
| `SettingsService` / `SettingsWindow` | Loads, validates, and saves settings, plus the settings UI |
| `TrayIcon`, `ToastWindow` | Small helper UI |
| `NativeMethods`, `MonitorService`, `KnownFolders` | All Win32 calls, kept in one place |

---

## 7. Project layout

```
PowerPen/
├─ PLAN.md
├─ README.md                 user guide + hotkey cheat sheet (Phase 6)
├─ PowerPen.slnx
├─ src/PowerPen/
│  ├─ PowerPen.csproj
│  ├─ app.manifest           Per-Monitor V2 DPI awareness
│  ├─ App.xaml(.cs)          entry point: single instance, logging, tray, --selftest, --render-ui
│  ├─ AppHost.cs             builds and wires the main objects (shared with the self-test)
│  ├─ Core/                  AppState, AppCommands, ToolType, InkStyle, UndoManager, UndoActions
│  ├─ Overlay/               OverlayManager, OverlayWindow, ShapeBuilder, ShapeTool, TextTool
│  ├─ Capture/               ScreenCapture, ScreenshotService, RegionSelector, RegionSelectWindow, ScreenshotFiles
│  ├─ Input/                 HotkeyService, GlobalHotkeys, Hotkey (parser), DrawModeKeys
│  ├─ Settings/              AppSettings (+ validation), SettingsService, SettingsStore
│  ├─ UI/                    ToolbarWindow/ViewModel, SwatchViewModel, ToolButton, ToastWindow, TrayIcon,
│  │                         SettingsWindow/ViewModel/Controller, ColorRowViewModel, HotkeyBox, Dialogs
│  ├─ Interop/               NativeMethods, MonitorService, PixelRect, FocusRestorer, KnownFolders, StartupRegistration
│  ├─ Diagnostics/           SelfTest, SyntheticInput, Log, VisualSnapshot
│  └─ Assets/                PowerPen.ico (generated by tools/make-icon.ps1)
├─ tests/PowerPen.Tests/     xUnit: UndoManager, Hotkey, DrawModeKeys, AppSettings, SettingsService, ShapeBuilder,
│                            ScreenshotFiles, SettingsViewModel
└─ tools/                    make-icon.ps1
```

---

## 8. Build phases

Each phase ends with something you can run and use.

### Phase 0 — Test the risky parts ✅ done (commit `8f9dadf`)
1. `git init`, then create the solution and WPF project with the Per-Monitor V2 manifest.
2. Show one overlay per monitor, correctly sized at mixed scaling.
3. Draw with a red pen in an InkCanvas.
4. Toggle Draw/Pointer mode with `Ctrl+Alt+D`, clear with `Ctrl+Alt+C`, and quit with `Ctrl+Alt+Q`. The quit hotkey is temporary until the tray icon exists.
5. `PowerPen.exe --selftest` runs an automated check on each monitor and writes a pass/fail report. It checks:
   - the overlay matches the monitor bounds;
   - WPF is using that monitor's DPI;
   - the ink shows up in a BitBlt capture;
   - clicks pass through in Pointer mode and are caught in Draw mode.
   Screenshots are checked in memory only and never saved. Keep this as a diagnostic for teammates' machines.
6. Manual: check drawing lag on the 2560×1440 monitor.

**Done when:** the self-test passes on every monitor, and drawing by hand feels lag-free.

### Phase 1 — Core drawing + toolbar ✅ done (manually tested 2026-10-07)
1. `AppState`, `AppCommands`, and `ToolbarWindow` with Pointer, Pen, Highlighter, Eraser, colors, thickness, Undo, and Clear.
2. `UndoManager` across all monitors.
3. `Esc` exits Draw mode and focus goes back to the previous app.
4. Self-test extended to cover undo, toolbar z-order, and focus.
   - ⚠ Windows only lets an app take focus right after the user interacts with it. So the self-test sends a real (synthetic) click to the Pen button and presses Esc.
   - It only clicks after confirming the spot belongs to the toolbar, and only presses Esc when a PowerPen overlay has focus.
   - The mouse moves for about a second while this runs.

**Done when:** you can annotate and undo using only the toolbar.

### Phase 2 — Hotkeys + settings file ✅ done (manually tested 2026-10-07)
1. `HotkeyService` for global hotkeys, plus Draw-mode keys.
2. `SettingsService` with JSON load/save and defaults.
3. Friendly messages for hotkey conflicts.
4. Added: Hide/show ink, with a toolbar eye button so hidden ink is never a mystery.
5. Added: tooltips show each button's shortcuts.
6. Unit tests (70) for the hotkey parser, Draw-mode keys, settings validation, and the settings file.
7. Self-test now also covers:
   - a global hotkey (sent as synthetic `Ctrl+Alt+Shift+F24`);
   - each Draw-mode key;
   - hiding ink;
   - errors that would otherwise only show as a popup.
8. Self-test clicks are now sent as one batch (move + click), so real mouse movement can't redirect them.
9. Manual test (2026-10-07): everything worked except `Ctrl+Alt+H`, which another app already owns.
   - The default is now `Ctrl+Alt+I`.
   - Fixed: settings were written with `+` instead of `+`.
10. Self-test flakiness: the toolbar click failed in 2 of ~10 runs, then passed 5 of 5.
    - Likely cause: real input during the test.
    - The report now logs what the toolbar received, so the next failure will show whether the click arrived.

**Done when:** everything from Phase 1 works from the keyboard, and settings survive a restart.

### Phase 3 — Shapes + text ✅ done (manually tested 2026-10-07)
1. Line, arrow, rectangle, and ellipse with live preview and Shift-constrain.
2. ~~Shapes flyout on the toolbar.~~ Replaced by the two-column toolbar (§5.8).
3. Text tool; eraser removes text too.
4. Unit tests (94 total) now cover the shape geometry (45° snapping, squares in every drag direction, closed ellipses, arrowhead limits) and the new keys.
5. Self-test adds a "Shapes and text" section, done with real (synthetic) input:
   - arrow drag;
   - Shift+drag square;
   - Ctrl+Z on a shape;
   - typing "hi" (H and I must not switch tools);
   - erasing text and undoing it;
   - Clear all + Undo.
   It passed 3 of 3 runs.

**Done when:** shapes and text can be undone and erased, and they appear in screenshots.

### Phase 4 — Screenshots ✅ done (manually tested 2026-10-07)
1. 📷 button: freeze all monitors, then drag for an area or click for a full monitor.
2. Save to Downloads (known folder), copy to clipboard, show toast.
3. Toolbar excluded from capture.
4. Unit tests (103 total) cover file naming, "never overwrite", PNG saving, and validation of the folder setting.
5. Self-test "Screenshots" section, run through the real 📷 button in **dry-run mode**: pictures stay in memory, nothing is written to disk, and the clipboard is never touched. It checks that:
   - the toolbar is invisible to captures (verified with a magenta line under it);
   - all monitors freeze;
   - a dragged area is the exact size and includes the ink;
   - a click captures the full monitor;
   - Esc cancels.
   It passed 3 of 3 runs.
6. Not covered by the self-test (manual): the real save to Downloads, the clipboard, and the toast.

**Done when:** the 📷 button produces a correct PNG for an area and for a full screen on all 3 monitors.

### Phase 5 — Polish ✅ built and verified by tests (manual test pending)
1. Tray icon, single instance, logging, app icon.
   - The icon is a blue rounded square with the pen symbol, generated by `tools/make-icon.ps1`.
2. Settings window: colors, hotkey recorder (click the box, press the keys), folder picker, toolbar-in-capture toggle, start with Windows.
   - Saving applies everything live.
   - Global hotkeys are paused while the window is open, because Windows would otherwise swallow the combinations before the recorder sees them.
   - Invalid values are listed in the window, and nothing is saved until they're fixed.
3. Toolbar collapse and position memory.
4. Unit tests (115 total) add the Settings window logic, settings cloning/copying (which catches any setting forgotten in `CopyFrom`), and the locked-file and atomic-save cases.
5. Self-test "Toolbar and settings" section, all in memory:
   - settings apply live;
   - capture-hiding can be switched off and on while running;
   - collapse and expand work with real clicks.
6. ⚠ **Night light breaks exact-color checks:** in the evening, every pixel check failed. An A/B test showed the Phase 4 build failed the same way, so it wasn't a code regression. The cause is Windows Night light: on this PC it's applied *before* capture, so magenta (255,0,254) was captured as (224,1,93).
   - The self-test now measures what the marker color looks like in captures before checking, and fails if the marker can't be seen at all.
   - **Product effect:** screenshots taken while Night light is on carry its warm tint on this setup. Windows applies it before PowerPen sees the pixels, so PowerPen can't fix it. Documented in the README.
7. Real-app smoke test: start; a second launch hands over and exits; `Ctrl+Alt+Q` closes; the log shows a clean start and close.

### Phase 6 — Package & share
1. Publish a single `.exe` (see §10).
2. **Gate:** set one monitor to a different scaling and run `--selftest`.
   - Mixed scaling was verified by hand at 150% on 2026-10-07 (Phase 1).
   - Re-check anyway, because Phases 3–5 add new windows (region select, toast, settings) that also need correct placement.
3. Test on a teammate's machine that has no .NET installed and a different monitor setup.
4. Write `README.md` with a hotkey cheat sheet.

---

## 9. Risks & gotchas

| Risk | Impact | Mitigation |
|---|---|---|
| Overlay offset or wrong size on mixed-scaling monitors | **High** for teammates' laptops | Place windows in physical pixels and re-place them after `DpiChanged`. Your monitors are all 100%, so set one to 125% temporarily and run `--selftest`. |
| Full-screen transparent windows lag on 4K | Medium | Measure in Phase 0. Fallback: a different transparency technique (DWM-based). Only if needed. |
| Draw mode steals focus (slideshow stops reacting to arrow keys) | Medium | Remember the foreground window and restore it on exit. Toolbar uses `NOACTIVATE`. |
| Toolbar ends up behind the overlays | Low | Re-apply topmost order on every mode change |
| Hotkey already taken by another app | Low | Friendly message; change it in Settings |
| Teams/Zoom **"share a window"** won't show the ink | Inherent | Share the **whole screen**. Epic Pen has the same limit. Document it in the README. |
| Exclusive full-screen apps (some games and video players) cover the overlay | Inherent | Not supported |
| Clipboard locked by another app | Low | Retry 3×; the file is still saved either way |
| SmartScreen warning on first run of a downloaded `.exe` | Low | Confirmed that teammates can run unsigned exes. Tell them to click *More info → Run anyway* once. |
| Night light (or HDR / color profiles) tints screenshots on some setups | Low | Windows applies it before capture on this PC; it can't be undone in PowerPen. Documented in the README. The self-test adapts to it. |

---

## 10. Distribution

```powershell
dotnet publish src/PowerPen -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

| Option | Size | Needs .NET installed? |
|---|---|---|
| **Self-contained single file** (recommended) | Tens of MB (measure in Phase 6) | No |
| Framework-dependent | ~1 MB | Yes, the .NET 10 Desktop Runtime |

The exe is unsigned, so the first run of a file downloaded through Teams, email, or OneDrive shows "Windows protected your PC" (click *More info → Run anyway*). Company policy allows unsigned exes.

---

## 11. Decisions

Answered 2026-10-07.

| # | Question | Decision |
|---|---|---|
| 1 | Input device | **Mouse-first.** Pen pressure stays on because it's free, but it's untested. |
| 2 | Monitors | **3 monitors, all at 100%:** 2560×1440 (main), 1920×1080 (right), 1080×1920 portrait (left). Mixed scaling is tested by temporarily changing one monitor. |
| 3 | Toolbar in Teams shares and screenshots | **Hidden from both.** |
| 4 | Screenshot / save hotkeys | **None.** The 📷 toolbar button only. It always saves to Downloads and copies to the clipboard. |
| 5 | Pointer mode keeps ink on screen | **Yes.** |
| 6 | Teammates can run an unsigned `.exe` | **Yes.** |
| 7 | "Text" | **Typed text**, not handwriting-to-text. |
| 8 | Other hotkey defaults (§5.6) | Kept as proposed. All are changeable in Settings. |

---

## 12. Coding conventions

- `/// <summary>` on every class, method, and property.
- Inline comments on complex blocks explain **why**, not what.
- Errors caused by the user (bad hotkey, unwritable folder, etc.) show a message with simple steps to fix them.
- All Win32 calls live in `Interop/NativeMethods.cs`. Nowhere else.
- Pure logic (`ShapeBuilder`, `HotkeyParser`, settings) stays free of UI code so it can be unit-tested.
