# KeyHint

A small always-on-top window that shows the **keyboard shortcut** of whatever menu item or button you just clicked, in any Windows app.

Click **File → Save** and it shows `Ctrl` + `S`. Click the Bold button and it shows `Ctrl` + `B`. Click something with no shortcut and it keeps the last answer and adds a faint "no shortcut" line.

## Run it

Double-click `dist\KeyHint.exe`. It also puts a teal **K** icon in the tray.

- **▾ button**: show the last 8 shortcuts you triggered, newest first.
- **✕ button**: hide the window. Click the tray icon to bring it back.
- **Tray icon → right-click**: pause, always on top, guess usual Windows shortcuts, start with Windows, exit.
- Drag the window anywhere; it remembers its place and never takes the keyboard focus away from the app you're clicking in.
- Only your own clicks count. Clicks that other programs make for you (macros, automation) are ignored.

## Where the shortcut comes from

In order, first one that exists wins:

| Source | Shown as | Example |
|---|---|---|
| The app publishes it for that item (UI Automation `AcceleratorKey`) | plain | Notepad → File → Save → `Ctrl S` |
| The app writes it in the item's label or tooltip | plain | a button named "Bold (Ctrl+B)" |
| Nobody published one, but it's a standard action | "This app didn't list one. This is the usual Windows shortcut for it." | a button named "Copy" → `Ctrl C` |
| A menu's Alt key | "A menu key: press Alt, then the letter." | File menu → `Alt` `F` |

The "usual Windows shortcut" guesses only cover names that mean the same thing in every app (Cut, Copy, Paste, Undo, Save, Find, Bold, Refresh, Rename, the title-bar buttons…). Names that differ between apps ("New", "Open", "Close") are never guessed. Turn the guesses off in the tray menu if you only want what the app itself says.

## What it can't see

KeyHint reads the same accessibility information screen readers use. Apps that draw their own controls and publish nothing give it nothing to read, so it stays quiet for them:

- Game engines and creative tools with custom UIs (Unity, Godot, Blender, Ableton Live, Resolume and similar) mostly show no shortcuts.
- Elevated (run as administrator) windows can't be read unless KeyHint is also run as administrator.
- Browser page content only reports shortcuts if the site publishes them; the browser's own menus and toolbar do.

## How it works

| Part | How |
|---|---|
| Hearing a click | A global low-level mouse hook (`Input/MouseHook.cs`). It only passes the position on; it never blocks the mouse. |
| What was clicked | Windows UI Automation: the element at that point, climbing past text and icons inside a button to the button itself (`Shortcuts/Resolver.cs`) |
| Reading the shortcut text | `Shortcuts/ShortcutText.cs` turns `Ctrl+Shift+Esc`, `Alt, F`, `Ctrl+K, Ctrl+C`, `Alt+Left Arrow` and the like into keycaps |

## Build

```bash
dotnet publish -c Release -r win-x64 --self-contained false -o dist
```

Checks without the window:

```bash
dist/KeyHint.exe --selftest out.txt
```

```bash
dist/KeyHint.exe --survey notepad out.txt
```

```bash
dist/KeyHint.exe --inspect 1200 800 out.txt
```

- `--selftest` runs the shortcut-text parser against 45 known spellings.
- `--survey <process name>` lists every button, menu item and tab of that running app's windows and what KeyHint would show for each. Open a menu first to include its entries.
- `--inspect X Y` writes everything Windows reports about the item at a screen position (physical pixels). Use it to find out why an item shows nothing.
- `--accept-injected` counts clicks made by other programs, and `--log file` writes every lookup (with what UI Automation saw) to a file. Both are for testing.

Settings live in `%APPDATA%\KeyHint\settings.json`.
