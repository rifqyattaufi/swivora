# Workspace Launcher

Pick a mode when you sign in to Windows, then switch modes and project workspaces on the fly.

- **Gaming** starts the apps you list (Discord, Steam, ...).
- **Work** has one **workspace per project**. Each workspace has its own app list (for example only one of them needs Docker) and opens on its **own Windows virtual desktop**.
- Several workspaces can run at once. Switching between them only changes the desktop, so nothing is closed or restarted.
- Switching mode closes the old mode's apps politely (like clicking X) and starts the new ones. Apps that both modes use stay open.
- Everything is driven from a tray icon, a picker window and global hotkeys. A settings window edits the config.

## Requirements

- Windows 11 24H2, build 26100.2605 or newer, for separate virtual desktops.
- On other Windows builds the app still runs, but workspaces share your current desktop and a notice says so.

## Install

Build a self-contained release (no .NET needed on the target PC):

```powershell
.\scripts\publish.ps1
```

Run `publish\WorkspaceLauncher.exe`. Keep `VirtualDesktopAccessor.dll` next to it. Turn on **Settings > General > Start when I sign in** to get the picker at every sign-in.

To build from source you need the .NET 10 SDK. `dotnet test` runs the tests; they open and close a few real windows (winver, charmap) and briefly flip a virtual desktop.

## Use

| Action | How |
|---|---|
| Open the picker | Tray icon click, `Ctrl+Alt+P`, or start the exe again |
| Gaming / Work | Picker, tray menu, or `Ctrl+Alt+M` to toggle |
| Jump to workspace N | `Ctrl+Alt+1` ... `Ctrl+Alt+9` (opens it if it is not running) |
| New workspace | Picker > Work > Add workspace, then fill in its apps |
| Close one workspace | Tray > Close workspace |

All hotkeys can be changed or turned off in **Settings > Hotkeys**. If another program already owns a hotkey you get a notice.

## Config

`%APPDATA%\WorkspaceLauncher\config.json` (set `WORKSPACE_LAUNCHER_CONFIG` to use another file). The settings window edits it; hand edits are picked up live.

```json
{
  "gaming": [ { "exe": "C:\Program Files\Steam\steam.exe" } ],
  "work": [
    {
      "name": "My project",
      "apps": [
        { "exe": "code", "args": "D:\projects\my-project" },
        { "exe": "C:\Program Files\Docker\Docker\Docker Desktop.exe",
          "stopCommand": "docker compose -f D:\projects\my-project\compose.yml down" }
      ]
    }
  ],
  "hotkeys": { "switchMode": "Ctrl+Alt+G" }
}
```

Per app: `exe`, `args`, `cwd`, `closeTimeoutSeconds` (default 10), `stopCommand` (runs through `cmd /c` after the app closes), and `ownership`:

- `adopt` (default): if the app is already running it becomes part of the mode or workspace and is closed when you leave.
- `leave`: an app that is already running is never touched.
- `launchOnly`: started if needed, never closed for you.

Apps that ignore the close request are not killed silently: you are asked first.

## Limits

- The virtual desktop API in Windows is undocumented and can change between builds. It is isolated behind one interface (`IDesktopBackend`).
- New windows are matched to a workspace by "appeared after launch", so a window you open by hand in the first few seconds can be pulled in.
- Apps with a single shared process (some browsers, editors) are closed through their windows. Use `leave` for apps you also use outside a workspace.
- Touchpad gestures are not supported. Windows' own four-finger swipe already moves between virtual desktops.

## Third-party

[VirtualDesktopAccessor](https://github.com/Ciantic/VirtualDesktopAccessor) (MIT) is bundled in `third_party/`. UI by [WPF UI](https://github.com/lepoco/wpfui).

## License

MIT
