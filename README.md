# Touch Beep

A small Windows desktop application that plays an audible beep when the user touches the screen. It works as a standalone system-wide layer: no need for individual applications or drivers to support beep-on-touch.

**Author:** Paul Admiraal  
**Copyright:** © ictadmiraal

---

## Why Touch Beep?

It is difficult to find an independent solution for **audible feedback on touch** when:

- The application itself does not implement beep-on-touch, or  
- The touch/digitizer driver does not provide it.

Touch Beep fills that gap. It uses Windows hooks and Raw Input to detect touch events system-wide and plays a configurable tone. It is useful for kiosks, POS terminals, Shell Launcher setups, and locked-down environments where you want consistent touch feedback without modifying each app or driver.

---

## Features

- **Beep on touch** — Low-level mouse hook and Windows touch signature so that only touch-screen taps trigger a beep (not mouse clicks). Runs elevated so it receives touch on the taskbar, Start menu, and all applications.
- **Configurable sound** — Toggle on/off, set frequency (200–4000 Hz), and choose wave type (sine, square, triangle).
- **Start with Windows** — Optional registry-based auto-start for current user or all users (when run as administrator).
- **Process filter** — Optional list of applications: beep only when the touched window belongs to one of them; empty list means beep everywhere. Configurable via GUI or CLI.
- **Touch device control** — List connected touchscreens (with primary/secondary monitor) and enable or disable them system-wide. Useful with multiple touch monitors. Changes persist across reboots. Available in GUI and CLI.
- **System tray** — Minimizes to tray; double-click the tray icon to show the window again.

---

## Requirements

- **OS:** Windows 10 or Windows 11  
- **Runtime (choose one):**
  - **.NET 8.0** — For the `net8.0-windows` build: [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or SDK to build).
  - **.NET Framework 4.8** — For the `net48` build: [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) (usually already present on Windows 10/11).

---

## Why two build targets?

The project targets **.NET 8.0 (Windows)** and **.NET Framework 4.8**:

- **.NET 8** — Default for new development; use when the target environment allows it.
- **.NET Framework 4.8** — For compatibility in **strict or locked-down environments** where only .NET Framework is approved or deployed. .NET Framework 4.8 is the last version of .NET Framework and is still **supported** (including security updates) as part of the Windows lifecycle.

---

## Build

Clone or download the repository, then build for the target you need.

**.NET 8 (Windows)**  
```bash
cd TouchBeep
dotnet build -c Release -f net8.0-windows
```  
Output: `bin\Release\net8.0-windows\TouchBeep.exe`

**.NET Framework 4.8**  
Install the [.NET Framework 4.8 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net48) if you are building (running only requires the runtime). Then:
```bash
cd TouchBeep
dotnet build -c Release -f net48
```  
Output: `bin\Release\net48\TouchBeep.exe`

**Build both**  
Omit the `-f` argument:
```bash
dotnet build -c Release
```

### GitHub Actions (CI)

On every push and pull request to `main`/`master`, GitHub builds the project on Windows and uploads artifacts:

- **TouchBeep-net48** — `bin/Release/net48/` (requires .NET Framework 4.8 on the target machine).
- **TouchBeep-net8.0-windows** — `bin/Release/net8.0-windows/` (requires [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) on the target machine).

To download a built exe: open the repo on GitHub → **Actions** → select the latest successful run → **Artifacts**, then download the archive you need and extract `TouchBeep.exe` (and any config/deps in the same folder).

**Publishing a release (so the Releases page shows a version):** push a version tag (e.g. `v1.0.0`). The **Release** workflow will build the project and create a GitHub Release with two zip assets: `TouchBeep-net48.zip` and `TouchBeep-net8.0-windows.zip`. From the repo root:

```bash
git tag v1.0.0
git push origin v1.0.0
```

Then open the repo → **Releases** to see the new release and download the executables.

---

## Usage

1. Run `TouchBeep.exe`. Windows will show a UAC prompt once; the app must run as administrator so the hook works on the taskbar, Start menu, and all programs.
2. Touch the screen (not the mouse); you should hear a short tone (desktop, taskbar, Start, Edge, etc.).
3. Use the window to turn sound on/off, set tone and wave type, manage the process filter, and (if needed) manage touch devices. Minimize to tray; use the tray icon to show the window again or exit.

If you enable “Start with Windows”, the first logon may show UAC again. In kiosk scenarios you can disable UAC or use auto-approve so the app starts without a prompt.

---

## Command-line reference

### Startup and install

| Command | Description |
|--------|-------------|
| `TouchBeep.exe` | Start the GUI. |
| `TouchBeep.exe install` | Add to Run for **all users** (requires admin). Exit code 2 if admin required. |
| `TouchBeep.exe install-user` | Add to Run for **current user** only. |
| `TouchBeep.exe uninstall` | Remove from all-users startup. |
| `TouchBeep.exe uninstall-user` | Remove from current-user startup. |
| `TouchBeep.exe help` | Print help. |

### Sound (saved to settings)

- `--tone <Hz>` — Frequency 200–4000 (e.g. `--tone 1000`).
- `--wave <name>` — `sine`, `square`, or `triangle`.
- Short form: `tone=800 wave=sine`.

### Process filter (beep only in selected apps; empty = all)

- `--filter-add <name>` — Add process name (e.g. `Calculator`).
- `--filter-remove <name>` — Remove from list.
- `--filter-clear` — Clear list (beep for all apps).
- `--filter-list` — Print current filter list and exit.

### Touch device control (requires admin; system-wide; persists across reboots)

- `--list-touch` — List touchscreens with index, monitor, and status.
- `--disable-touch <n>` — Disable touchscreen #n (use index from `--list-touch`).
- `--enable-touch <n>` — Re-enable touchscreen #n.

### Examples

```bash
TouchBeep.exe install --tone 1000 --wave sine --filter-add Calculator
TouchBeep.exe --filter-add notepad --filter-add mspaint
TouchBeep.exe --list-touch
TouchBeep.exe --disable-touch 2
TouchBeep.exe --enable-touch 2
```

---

## Deployment (e.g. PDQ / GPO)

1. Copy the built exe to the target (e.g. `C:\Program Files\TouchBeep\TouchBeep.exe`).
2. Run as Administrator: `TouchBeep.exe install` so it starts at every user logon.
3. Optionally preconfigure sound or filters: `TouchBeep.exe install --tone 800 --wave sine`.

---

## Reset repository history (clear all commits and re-upload)

To wipe all commit history and push the current state as a single fresh commit to GitHub (e.g. after removing sensitive data or cleaning up):

**Option 1 — Run the script (PowerShell, from repo root):**
```powershell
.\scripts\reset-and-push.ps1
```

**Option 2 — Run these commands manually:**

```bash
# Create a new orphan branch (no history) with the current files
git checkout --orphan temp-branch
git add -A
git commit -m "Initial commit"

# Replace the default branch (e.g. main) with this clean history
git branch -D main
git branch -m main

# Overwrite the remote (destroys history on GitHub for this repo)
git push -f origin main
```

If your default branch is `master`, use `master` instead of `main`. **Warning:** This removes all history on the remote; anyone who has cloned the repo will need to re-clone or reset their copy.

---

## License and copyright

Copyright (c) ictadmiraal. See repository or distribution for applicable license terms.
