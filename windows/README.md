# Bambu Bar for Windows

An independent Windows desktop implementation of Bambu Bar. The Swift macOS app is unchanged. The Windows app uses .NET 10, Avalonia and MQTTnet and talks directly to the printers; it needs neither Docker nor a separately installed .NET runtime when installed.

Fork: [opedromartins/bambu-bar-windows](https://github.com/opedromartins/bambu-bar-windows). See the [implementation and macOS parity notes](IMPLEMENTATION.md) and [Windows changelog](CHANGELOG.md). Successful runs of the [Windows workflow](https://github.com/opedromartins/bambu-bar-windows/actions/workflows/windows.yml) provide the installer, portable ZIP, checksums and preview images in the **Bambu-Bar-windows-x64** artifact. The workflow does not publish GitHub releases.

## Install and connect

Target: Windows 10 **22H2** / Windows 11, Intel or AMD x64. Packages are unsigned. Windows 10 compatibility must be checked on the target edition; Microsoft's current .NET support policy only includes Windows 10 editions still in servicing.

Run `Bambu-Bar-1.3.1-windows-x64-setup.exe`, or extract the portable ZIP and run `BambuBar.exe` with all its accompanying files. The installer is per-user and requires no administrator rights. Find the app under **Bambu Bar** in the Start menu. Choose **Quit** from the tray before upgrading or uninstalling; the installer checks for running instances. Uninstall through Windows Settings; printer settings remain in `%LOCALAPPDATA%\Bambu Bar`.

In **Settings**, printers on the local network appear under **Found on your network**. Choose **Add** to fill the name, IP, serial and model, then enter the eight-character LAN access code and connect. Discovery also appears on the add-printer form, without replacing what you are typing when new announcements arrive. **Search again** requests fresh replies. Already configured printers are marked in the list. The printer screen shows the access code under **Settings → Network**; discovery cannot retrieve it.

You can still enter a printer manually in **Settings → Add printer**. Use port **8883** for real printers. Model identification defaults to the serial prefix; select a model explicitly when necessary. Add as many printers as needed and mark one **Primary** for the tray tooltip.

Discovery listens for Bambu SSDP-style announcements on UDP **2021/1990** and sends M-SEARCH on each active IPv4 multicast interface. A dedicated ephemeral reply port lets active search coexist with slicers. Search repeats every 30 seconds and on network changes; entries expire after two minutes without an announcement. Connect the PC to the same LAN/Wi-Fi as the printer and allow Bambu Bar on **private networks** if Windows Firewall asks. Guest networks or VLAN isolation can block discovery; manual IP entry remains available. The app does not create firewall rules or require administrator access.

For saved printers that are offline or at an address with a mismatched certificate, an announcement of a different IP triggers a credential-free TLS check. Only a valid Bambu certificate matching the saved serial allows the address to be updated, persisted and reconnected. Custom names and protected access codes are retained; an announcement alone never authenticates a device or receives the code.

The **Printer** page shows the job, translated stage/pause reason, progress, layers, projected time remaining, stable finish time and temperatures. It also shows legacy/packed fan speeds, door state, Wi-Fi signal/quality, SD card, active nozzle, nozzle material/diameter, build volume, model temperature limits and available printer firmware updates. Unsupported or missing readings stay unknown. **Filament** shows AMS trays, materials, colors and remaining grams when the printer supplies net spool weight. **All printers** puts paused and failed jobs first. Pause, resume, stop and speed require **LAN Only + Developer Mode** on the printer; the light can work without them. Stopping a print requires confirmation. A timed-out command may have executed: inspect the printer state before retrying.

Closing the window keeps the app in the system tray. Click the tray icon to toggle a compact **Quick panel**, with the job, progress, time remaining, temperatures, AMS and print controls. The grid button lists all printers; the picker switches the displayed printer. The panel opens on the clicked monitor, stays inside its working area with DPI scaling, and closes when clicking outside or pressing Escape. **Open window** opens the full interface. Right-click the tray icon for **Quick panel**, **Open Bambu Bar** and **Quit**. Quit stops all connections.

The tray printer glyph fills as the primary printer progresses and gains an alert badge for paused/failed/offline states; its tooltip includes the job state, percentage and time remaining. The Windows notification area does not provide a persistent text label beside tray icons, so the text is available in the tooltip and panel. Opening the panel initially follows the configured primary printer. Run `BambuBar.exe --tray` to start with only the tray icon; you can still open the full window from the panel or context menu.

All 18 languages present in `Resources/Localizable.xcstrings` are available. The Windows app reads that same catalogue at runtime, including positional placeholders and language-specific plural forms, rather than maintaining separate translated UI strings. HMS error catalogues follow the selected language with the same English fallback as macOS. Light/dark/system theme is configurable. Missing measurements display `—`; an offline printer retains its last report, clearly marked stale. The quick panel shares the existing sessions and commands, including stop confirmation, firmware locks and acknowledgement errors; it works while the main window is hidden.

The interface uses translucent surfaces, subtle borders and a tinted glass background. On Windows it requests **Acrylic Blur**, with Mica/blur alternatives when available and a readable solid fallback when the compositor does not support transparency. The native effect depends on Windows and its transparency settings; Linux/Xvfb previews show the layout and alpha layers, not the Windows desktop blur. The main and quick panels use interactive 3D geometry generated from the original Blender authoring file. Drag to rotate, scroll to zoom and double-click to reset; arrow keys rotate, +/- zoom and Home resets when the view has focus. The part grows in slices, the bed/gantry follows progress, illustrative toolhead motion runs during printing and the chamber light/spool colors follow status. Rotation and zoom survive status refreshes. Animations stop when views leave the visual tree and do not redraw hidden windows. Compact fleet thumbnails still use the render layers.

The quick panel uses Acrylic/blur/transparent fallbacks and applies its 20-DIP rounded outline to the native HWND as well as the content. The native region is updated when opened, resized or moved between DPI scales, so the backdrop/fallback cannot fill the corners with a black rectangle. Windows smoke checks inspect all four excluded corners, the retained center/edges, resizing and reopening via [`SetWindowRgn`/`GetWindowRgn`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowrgn); PNG previews alone cannot verify the native outline.

Printer settings are saved locally. Access codes are encrypted with Windows **DPAPI**, scoped to the current user. Copying the settings to another Windows account will not transfer usable credentials; re-enter the access codes. The app makes no cloud requests. Printer certificates must chain to the bundled Bambu authorities and identify the configured serial before MQTT credentials are sent.

## Notifications, controls and timing

**Settings → Notifications** configures finished, failed/canceled/error, paused and optional started notifications. Native Windows toasts are local and work while the main window is hidden. Clicking a toast opens its printer. The first report after connection/reconnection establishes a baseline; repeated status packets do not repeat notifications, while a cleared error that returns can notify again. Demo/emulator runs never post native notifications. Windows notification settings and Focus Assist still control whether banners appear. The installer needs no extra notification runtime; uninstall removes the app's notification registration.

Manual setup can leave the serial blank: **Connect** performs a credential-free TLS handshake to read the serial from a certificate signed by a bundled Bambu authority, fills the model/name when possible, then uses the entered access code for MQTT. A self-signed or mismatched certificate is never accepted in production.

AMS panels include humidity percent or the reported 1–5 scale, drying time left, multicolor swatches and recommended nozzle ranges. HMS issues have severity labels and a Wiki link. A canceled print is distinct from a failed print in status, notifications and tray alerts.

Remaining time counts down only for an online printing job. Paused/offline values are held; unknown/preparation estimates stay unknown, and elapsed time is clamped if the system clock moves backward. Unchanged temperature/fan packets do not move the finish-time anchor. The estimate is refreshed when the printer changes job/progress/remaining time or resumes.

| Shortcut | Action |
| --- | --- |
| Ctrl+1 / Ctrl+2 / Ctrl+3 / Ctrl+4 | Printer / Filament / All Printers / Settings |
| Ctrl+, | Settings |
| Ctrl+R | Refresh the selected printer |
| Ctrl+L | Toggle chamber light where supported |
| Ctrl+Shift+P | Pause/resume the active print |
| Ctrl+Shift+S | Stop, with confirmation |

The shortcuts also work from the quick panel. Firmware locks and offline checks still apply. Stop confirmation is owned by the visible panel even when the main window is hidden.

## Build using Docker only

Install Docker Desktop with its Linux containers backend. No local .NET SDK, Visual Studio, Python, Blender, MQTT broker or NSIS is required. The Windows target includes its WinRT notification dependencies; a plain .NET target is retained for Linux UI checks. From the repository root in PowerShell:

```powershell
.\windows\build.ps1 build
.\windows\build.ps1 test
.\windows\build.ps1 integration
.\windows\build.ps1 preview
.\windows\build.ps1 package
```

The wrapper builds the tool image automatically. `integration` starts and then stops the dedicated Docker fleet. `preview` exercises each page in both languages/themes under Xvfb and writes page screenshots plus a compact-window snapshot. For a Unix shell or CI:

```sh
docker compose -f windows/compose.yaml build tools
docker compose -f windows/compose.yaml run --rm tools sh windows/scripts/build.sh build
docker compose -f windows/compose.yaml run --rm tools sh windows/scripts/build.sh test
docker compose -f windows/compose.yaml up --build --force-recreate -d fleet
docker compose -f windows/compose.yaml run --rm --use-aliases tools sh windows/scripts/build.sh integration
docker compose -f windows/compose.yaml run --rm tools sh windows/scripts/build.sh preview
docker compose -f windows/compose.yaml run --rm tools sh windows/scripts/build.sh package
docker compose -f windows/compose.yaml down
```

On Linux with a different UID/GID, export `LOCAL_UID=$(id -u)` and `LOCAL_GID=$(id -g)` first. Linux containers generate Windows binaries; they cannot execute the Windows GUI or Windows DPAPI. Windows CI supplies the native smoke test.

The build script creates private temporary XDG runtime, data, configuration and
cache directories and removes them on exit. This supports host UIDs without a
matching account inside the SDK image, avoiding the `NuGet-Migrations` first-run
failure and Avalonia build-service writes to the unwritable `/.local` directory.

Artifacts in `build/windows/`:

- `Bambu-Bar-1.3.1-windows-x64-setup.exe`: per-user NSIS installer, with bundled .NET runtime.
- `Bambu-Bar-1.3.1-windows-x64-portable.zip`: complete portable directory.
- `SHA256SUMS`: hashes of the installer and portable ZIP.
- `app/`: unpacked self-contained application.
- `preview.png`: demo screenshot, after the preview check; other `preview-*.png` files show 3D families/orbit, telemetry/errors, discovery and the quick panel.

Verify a download with `Get-FileHash <file> -Algorithm SHA256` against `SHA256SUMS`. The five compressed 3D meshes are committed under `Assets/Models`; regenerate them after changing the original artwork with `.\windows\build.ps1 models`. This runs `windows/scripts/export-models.py` in Blender inside Docker, without altering the macOS USDZ/PNG assets. Package versions are taken from `windows/Directory.Build.props`. NuGet versions are fixed in project files and committed lockfiles; normal commands restore in locked mode. Use the `restore` action only when deliberately updating dependencies. The SDK container is pinned by digest; OS tooling is installed in the image.

## Development and emulators

```powershell
.\build\windows\app\BambuBar.exe --demo
.\build\windows\app\BambuBar.exe --emulator
```

Demo mode uses in-memory printers and simulated controls, without network access or persistence. Emulator mode permits only loopback printer addresses, bypasses certificate verification only for those local sessions and saves nothing; it listens on loopback UDP **2121** without searching the real LAN. The Compose integration fleet advertises to the `tools` container on UDP 2021, so run integration tests with `--use-aliases`. Its advertised addresses are resolved to container IPs, while MQTT tests can still use the service name `fleet`. For a desktop emulator session, add the published Docker fleet manually using `127.0.0.1`, port **18801**, serial **22EF00000000001**, access code **FLT00001**. The second printer uses port 18802, serial `01PF00000000002`, code `FLT00002`; see `scripts/fleet/fleet.py` for the remaining entries. Never use emulator mode with a real printer.

Only the test executable can opt into the Compose service name `fleet`, which is required for container-to-container tests. Production connections always verify certificates. Recreate the fleet before every integration run: testing the stop command cancels its initial print. The PowerShell wrapper does this automatically. The application core owns sessions and report parsing; the desktop project owns Avalonia UI and Windows credential storage. Tests are a dependency-free executable that exits nonzero on any failed assertion.

## Validation

`test` covers full/incremental reports, empty arrays, AMS removal, real P2S fixtures, legacy/packed temperatures, two nozzles, missing data, filaments, certificate chains/serial/expiry, invalid JSON, credential-before-TLS rejection, authentication refusal, reconnect, rejected commands and acknowledgement timeout. Discovery tests cover notifications/search replies, URI and raw endpoints, rejected malformed packets, live UDP reception, deduplication, address changes, expiry, socket disposal and TLS identity probes without MQTT login. `integration` additionally checks all 11 online fleet announcements, every control, H2D hardware, bad credentials, untrusted TLS and the offline printer.

The UI smoke check also renders the quick panel in both themes and fleet view, checks placement on negative-coordinate monitors at 100%/150% scale, and exercises simulated pause/resume/speed/light/stop with the main window hidden, including the owned stop-confirmation dialog. Smoke checks require `--demo`, so they cannot send commands to real printers. The smoke check also exercises the five real 3D mesh families, orbit/zoom preservation, growing print slices and keyboard actions. Core tests cover notification transitions/baselines, time projection, telemetry, multicolor AMS/drying, error severity, xcstrings placeholders/plurals and IP-only TLS identity. The Windows CI job checks installation, packaged runtime, DPAPI persistence, the native toast registration/delivery API, UI rendering and uninstallation on Windows 11. It does **not** replace manual testing on Windows 10 or real printers. Before a release, record these checks on both Windows 10 22H2 and Windows 11:

- Install on a clean x64 PC without .NET, launch from Start, close to tray, reopen and quit.
- Open the quick panel from the tray on each monitor/DPI, click outside/Escape, change printers, confirm/cancel a stop, open the main window and check taskbar positioning and Acrylic Blur. Start with `--tray` and confirm that **Open window** still works.
- Add/edit/delete multiple printers, change the primary printer, restart, and verify saved credentials reconnect.
- Discover physical printers over Wi-Fi/Ethernet with and without a running slicer, fill the form from an announcement, preserve typed access codes during discovery updates, change networks, and confirm a saved printer follows a real DHCP address change only after TLS identity verification.
- Inspect all pages at 100% and 150% scaling, in Portuguese/English and both themes; check other catalogue languages for clipping and missing translations.
- Check a real printer's reports, AMS and permitted controls, command refusal without Developer Mode, and recovery after network loss/sleep.
- Verify toast banners/clicks and notification preferences on native Windows, including first-connect suppression, repeated reports and cancellation.
- Exercise 3D drag/zoom/reset and live progress on all model families, and keyboard shortcuts with both the main window and quick panel.
- Run the portable app, verify checksums, uninstall and confirm program and notification-registration removal.

Slicer import, print history, desktop widgets, open-at-login registration, camera and app update checks remain outside this version. The 3D view is an approximate visualization, not a live G-code toolpath. No certificate for Authenticode signing is included. MIT license and original credits apply; existing certificates, filament tables, HMS texts and artwork are reused from the repository.
