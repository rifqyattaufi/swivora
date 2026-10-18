<p align="center">
  <img src="assets/brand/swivora-icon.png" width="140" alt="Swivora" />
</p>

<h1 align="center">Swivora</h1>

<p align="center"><b>Your Workspace, One Switch.</b></p>

<p align="center">
  <img alt="Windows 11" src="https://img.shields.io/badge/Windows-11-3B82F6" />
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-8B5CF6" />
  <img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-1E1B4B" />
  <a href="https://ko-fi.com/rifqyattaufi"><img alt="Support on Ko-fi" src="https://img.shields.io/badge/Ko--fi-Support-FF5E5B?logo=ko-fi&logoColor=white" /></a>
  <a href="https://saweria.co/rifqyattaufi"><img alt="Support on Saweria" src="https://img.shields.io/badge/Saweria-Support-FAAE2B" /></a>
</p>

Swivora is a small Windows 11 tray app. Pick a mode when you sign in, then switch modes and project workspaces on the fly. Every workspace has its own apps and opens on its own Windows virtual desktop.

## Built for your flow

- **Modes, as many as you like.** *Gaming*, *Work*, *Streaming*, anything. Each mode has a name and an icon (an emoji or a picture), shown as a list or as a grid of big icons.
- **Apps modes** (like *Gaming*): one list of apps, started together (Discord, Steam, ...).
- **Workspaces modes** (like *Work*): one **workspace per project**. Each workspace has its own app list (for example only one of them needs Docker) and opens on its **own Windows virtual desktop**.
- **Several workspaces at once.** Switching between them only changes the desktop, so nothing is closed or restarted.
- **Polite switching.** Changing **mode** closes the old mode's apps like clicking X, then starts the new ones. You choose: always close, ask first, or leave them running. Apps both modes use stay open.
- **Easy setup.** Pick apps from your Windows app list, with names and icons. Common programs come with ready-made arguments, and Swivora warns you when an app already starts with Windows.
- **Global hotkeys** for the picker, the next mode and workspaces 1 to 9.
- **Open source (MIT).** Everything is driven from a tray icon, a picker window and hotkeys. A settings window edits the config.

## Requirements

- Windows 11 24H2, build 26100.2605 or newer, for separate virtual desktops.
- On other Windows builds the app still runs, but workspaces share your current desktop and a notice says so.

## Install

Build a self-contained release (no .NET needed on the target PC):

```powershell
.\scripts\publish.ps1
```

Run `publish\Swivora.exe`. Keep `VirtualDesktopAccessor.dll` next to it. Turn on **Settings > General > Start when I sign in** to get the picker at every sign-in.

To build from source, clone the repo (`git clone https://github.com/rifqyattaufi/swivora.git`) and install the .NET 10 SDK. `dotnet test` runs the tests; they open and close a few real windows (winver, charmap) and briefly flip a virtual desktop.

## Use

| Action | How |
|---|---|
| Open the picker | Tray icon click, `Ctrl+Alt+P`, or start the exe again |
| Start a mode | Picker or tray menu. `Ctrl+Alt+M` cycles to the next mode |
| Jump to workspace N | `Ctrl+Alt+1` ... `Ctrl+Alt+9` (opens it if it is not running; counted across all workspaces modes) |
| Jump to mode N | Off by default. Set `Open mode N` in Settings > Hotkeys |
| New mode | Settings > Apps > Add mode, or tray > Add mode |
| New workspace | Picker > a workspaces mode > Add workspace, then fill in its apps |
| Close one workspace | Tray > Close workspace |
| Add an app | Settings > Apps > Add app. Pick the program from **Installed apps** (your Start menu list) or **Browse**. For common programs (File Explorer, VS Code, Chrome, Edge, Firefox, Windows Terminal, Steam and more) a **Common arguments** list fills the arguments for you. You can always type your own. |

All hotkeys can be changed or turned off in **Settings > Hotkeys**. If another program already owns a hotkey you get a notice.

## Config

`%APPDATA%\Swivora\config.json` (set `SWIVORA_CONFIG` to use another file). If you used this app under its old name (Workspace Launcher), your config is copied over on the first start. The settings window edits it; hand edits are picked up live. Files from the first version (`"gaming"` and `"work"` at the top level) are still read and become the two modes Gaming and Work.

```json
{
  "modes": [
    {
      "name": "Gaming",
      "kind": "apps",
      "apps": [ { "exe": "C:\\Program Files (x86)\\Steam\\steam.exe" } ]
    },
    {
      "name": "Work",
      "kind": "workspaces",
      "workspaces": [
        {
          "name": "My project",
          "apps": [
            { "exe": "code", "args": "D:\\projects\\my-project" },
            { "exe": "C:\\Program Files\\Docker\\Docker\\Docker Desktop.exe",
              "stopCommand": "docker compose -f D:\\projects\\my-project\\compose.yml down" }
          ]
        }
      ]
    }
  ],
  "hotkeys": { "switchMode": "Ctrl+Alt+G" }
}
```

Top level `onSwitch` decides what happens to the running mode's apps when you switch to another mode: `"close"` (default), `"ask"` (a Yes / No / Cancel question) or `"keep"` (leave them running and stop managing them). Apps the new mode also uses always stay open. Set it in Settings > General.

Per mode: `name`, `kind`, `icon` (an emoji, or `img:icons\file.png` for a picture kept beside the config). Top level: `pickerStyle` (`"grid"` for big icons with the name below, anything else is the list). Both are set in Settings (Apps > Icon, General > Mode picker). Emoji are drawn in one color; use a picture for a full-color icon.

Per app: `exe`, `name` (what lists show, default is the program's own product name), `args`, `cwd`, `closeTimeoutSeconds` (default 10), `stopCommand` (runs through `cmd /c` after the app closes), and `ownership`:

- `adopt` (default): if the app is already running it becomes part of the mode or workspace and is closed when you leave.
- `leave`: an app that is already running is never touched.
- `launchOnly`: started if needed, never closed for you.

Apps that ignore the close request are not killed silently: you are asked first.

## Limits

- The virtual desktop API in Windows is undocumented and can change between builds. It is isolated behind one interface (`IDesktopBackend`).
- New windows are matched to a workspace by "appeared after launch", so a window you open by hand in the first few seconds can be pulled in.
- Apps with a single shared process (some browsers, editors) are closed through their windows. Use `leave` for apps you also use outside a workspace.
- Touchpad gestures are not supported. Windows' own four-finger swipe already moves between virtual desktops.

## Brand

Swivora's mark is three leaning bars in a blue to violet gradient. Colors: blue `#3B82F6`, violet `#8B5CF6`, indigo `#1E1B4B`, light gray `#E5E7EB`. The icons in `assets/brand/` and the app icon are drawn by `scripts/make-brand-assets.py` (needs Pillow and numpy).

## Support

Swivora is free. If it saves you time, you can support its development on [Ko-fi](https://ko-fi.com/rifqyattaufi) or [Saweria](https://saweria.co/rifqyattaufi), or open Settings > General in the app.

## Third-party

[VirtualDesktopAccessor](https://github.com/Ciantic/VirtualDesktopAccessor) (MIT) is bundled in `third_party/`. UI by [WPF UI](https://github.com/lepoco/wpfui).

## License

MIT
