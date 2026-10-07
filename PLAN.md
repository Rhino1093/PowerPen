# PowerPen — Build Plan

**Status:** v0.2 — 2026-10-07. Open questions answered (see [§11](#11-decisions)). Phase 0 in progress.

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
  This never exercises mixed scaling, but teammates' laptops (usually 125–150%) will. Test it once by temporarily setting one monitor to 125%.

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
| Eraser | `EraseByStroke` removes whole strokes, like Epic Pen. It also hit-tests text boxes. |
| Line / Arrow / Rectangle / Ellipse | `EditingMode=None`. We handle mouse down/move/up ourselves and show a live preview. On release, the shape is converted into normal `Stroke`(s). **Shift** constrains it (45° lines, squares, circles). |
| Text | Click to place a `TextBox`. `Enter` or clicking away turns it into a `TextBlock` on the InkCanvas. `Shift+Enter` adds a new line, `Esc` cancels. |

**Why shapes become strokes:** eraser, undo, clear, and screenshots then work on shapes with zero special cases.

### 5.5 Undo / redo

- One app-wide stack that covers all monitors, so `Ctrl+Z` undoes the last thing you did no matter where it was.
- Actions: `AddStrokes`, `RemoveStrokes`, `AddText`, `RemoveText`, `ClearAll` (keeps a snapshot).
- Recorded from `StrokeCollection.StrokesChanged`. A flag pauses recording while an undo or redo is running.
- **Clear is undoable**, so an accidental clear isn't fatal.

### 5.6 Hotkeys

There are two layers. Global single-letter hotkeys would break typing in every other app, so plain-letter keys only work in Draw mode.

**Global** (`RegisterHotKey`, work from any app)

| Action | Default |
|---|---|
| Toggle Draw / Pointer mode | `Ctrl+Alt+D` |
| Pen in color 1–8 (also enters Draw mode) | `Ctrl+Alt+1`…`8` |
| Clear all ink | `Ctrl+Alt+C` |
| Hide / show ink | `Ctrl+Alt+H` |

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

**If a hotkey is already taken** by another app, PowerPen keeps running and shows:
> "Ctrl+Alt+D is already used by another app. Open Settings → Hotkeys and pick a different combination."

### 5.7 Screenshots

**Flow:** one toolbar button, no hotkey.
1. Clicking 📷 immediately captures **every** monitor. This freezes the image, so what you see is what you get.
2. Each monitor shows its frozen image slightly dimmed, with the hint "Drag to capture an area · Click to capture this screen · Esc to cancel."
   - **Drag** a rectangle → captures that area.
   - **Single click** → captures that whole monitor. This also lets you click 📷 on one monitor and capture a different one.
3. Crop, save as PNG, copy to the clipboard, and show a toast: "Saved PowerPen_2026-10-07_143205.png." Clicking the toast opens the folder with the file selected.

**Details**
- **Capture method:** GDI `BitBlt` with `SRCCOPY | CAPTUREBLT`, called directly. `CAPTUREBLT` is what includes the transparent ink overlay. It captures physical pixels, so you get full native resolution on every monitor.
  - ⚠ `Graphics.CopyFromScreen` can't be used: it throws if `CaptureBlt` is combined with any other flag. The Phase 0 self-test found this.
- **Toolbar never appears in screenshots:** it's set with `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, so there's no hide-and-flash trick. Side effect: the toolbar is also hidden from Teams/Zoom shares. This is a setting.
- **Downloads folder:** found with `SHGetKnownFolderPath(FOLDERID_Downloads)`, **not** `%USERPROFILE%\Downloads`, because corporate OneDrive can redirect it. The folder can be overridden in Settings.
- **v1 limit:** a region must be on a single monitor.

### 5.8 Toolbar UI

```
┌──────┐
│  ⠿   │  drag handle
├──────┤
│  ↖   │  Pointer mode (click-through)
│  ✎   │  Pen
│  ▮   │  Highlighter
│  ⌫   │  Eraser
│  ▭ ▸ │  Shapes ▸ flyout: Line · Arrow · Rectangle · Ellipse
│  T   │  Text
├──────┤
│ ● ●  │
│ ● ●  │  8 color swatches (active one has a ring)
│ ● ●  │
│ ● ●  │
├──────┤
│  ━   │  Thickness (click cycles S / M / L)
│  ↶   │  Undo
│  🗑   │  Clear all
│  📷  │  Screenshot (then drag an area, or click a screen)
│  ⚙   │  Settings
│  ▴   │  Collapse to a single button
└──────┘
```

- Floating and draggable. Remembers its position, and snaps back to the primary monitor if that screen is gone.
- The active tool is highlighted. Tooltips show the hotkey, e.g., "Pen (P)", so people learn the keys by using the toolbar.
- Icons come from the **Segoe Fluent Icons** font, which is built into Windows 11, so there are no image files to manage.
- `WS_EX_NOACTIVATE`: clicking the toolbar doesn't steal focus from PowerPoint or other apps.
- Always sits above the overlays. Topmost is re-applied after every mode change.

**Tray icon menu:** Show/Hide toolbar · Settings · Open screenshots folder · Exit.

### 5.9 Settings

The settings file is `%AppData%\PowerPen\settings.json`:

```json
{
  "colors": ["#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#8E24AA", "#000000", "#FFFFFF"],
  "thickness": { "small": 2, "medium": 4, "large": 8 },
  "highlighterWidthMultiplier": 3,
  "screenshotFolder": "",
  "copyScreenshotToClipboard": true,
  "hideToolbarFromCapture": true,
  "startWithWindows": false,
  "toolbar": { "x": 40, "y": 200, "collapsed": false },
  "hotkeys": {
    "toggleDraw": "Ctrl+Alt+D",
    "clearAll": "Ctrl+Alt+C",
    "toggleInkVisible": "Ctrl+Alt+H"
  }
}
```

- An empty `screenshotFolder` means Downloads.
- If the file is missing, defaults are used.
- If the file is corrupt, it's renamed to `settings.bad.json`, defaults are loaded, and the user sees: "Settings file was unreadable. Defaults restored; your old file was saved as settings.bad.json."

### 5.10 Errors & logging

- A global exception handler shows a friendly message and writes details to `%AppData%\PowerPen\logs\`. This lets you debug problems on a teammate's machine.
- Single instance via a named Mutex. Launching PowerPen a second time just brings up the existing toolbar instead of creating duplicate overlays and hotkey conflicts.

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
│  ├─ App.xaml(.cs)
│  ├─ Core/                  AppState, AppCommands, ToolType, UndoManager
│  ├─ Overlay/               OverlayManager, OverlayWindow, ShapeBuilder, TextTool
│  ├─ Capture/               ScreenshotService, RegionSelectWindow
│  ├─ Input/                 HotkeyService, HotkeyParser, DrawModeKeys
│  ├─ Settings/              AppSettings, SettingsService, SettingsWindow
│  ├─ UI/                    ToolbarWindow, ToolbarViewModel, ToastWindow, TrayIcon
│  ├─ Interop/               NativeMethods, MonitorService, KnownFolders
│  └─ Assets/                PowerPen.ico
└─ tests/PowerPen.Tests/     xUnit: ShapeBuilder, HotkeyParser, settings round-trip, file naming
```

---

## 8. Build phases

Each phase ends with something you can run and use.

### Phase 0 — Test the risky parts
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

### Phase 1 — Core drawing + toolbar
1. `AppState`, `AppCommands`, and `ToolbarWindow` with Pointer, Pen, Highlighter, Eraser, colors, thickness, Undo, and Clear.
2. `UndoManager` across all monitors.
3. `Esc` exits Draw mode and focus goes back to the previous app.

**Done when:** you can annotate and undo using only the toolbar.

### Phase 2 — Hotkeys + settings file
1. `HotkeyService` for global hotkeys, plus Draw-mode keys.
2. `SettingsService` with JSON load/save and defaults.
3. Friendly messages for hotkey conflicts.

**Done when:** everything from Phase 1 works from the keyboard, and settings survive a restart.

### Phase 3 — Shapes + text
1. Line, arrow, rectangle, and ellipse with live preview and Shift-constrain.
2. Shapes flyout on the toolbar.
3. Text tool; eraser removes text too.

**Done when:** shapes and text can be undone and erased, and they appear in screenshots.

### Phase 4 — Screenshots
1. 📷 button: freeze all monitors, then drag for an area or click for a full monitor.
2. Save to Downloads (known folder), copy to clipboard, show toast.
3. Toolbar excluded from capture.

**Done when:** the 📷 button produces a correct PNG for an area and for a full screen on all 3 monitors.

### Phase 5 — Polish
1. Tray icon, single instance, logging, app icon.
2. Settings window: colors, hotkey recorder (click the box, press the keys), folder picker, toolbar-in-capture toggle, start with Windows.
3. Toolbar collapse and position memory.

### Phase 6 — Package & share
1. Publish a single `.exe` (see §10).
2. Test on a teammate's machine that has no .NET installed and a different monitor setup.
3. Write `README.md` with a hotkey cheat sheet.

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
