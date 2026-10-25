# Windows implementation

This fork adds a Windows application alongside the upstream Swift app at commit
`2868f3212f1c79272b0f64acb9dd2f8948a3c6e5` (Bambu Bar 1.3.1). It retains the
original MIT license, authorship and shared resources. The Windows package version
currently follows that upstream version; it does not mean the two apps have identical features.

## Architecture

| Location | Responsibility |
| --- | --- |
| `BambuBar.Core/` | Printer models/capabilities, full and incremental report merging, stages, AMS/filaments, HMS, time projection, notification transitions, discovery, TLS identity and MQTT sessions. No Avalonia dependency. |
| `BambuBar.Desktop/` | Avalonia pages and quick panel, themes/transparency, interactive 3D, keyboard actions, tray, native toasts, settings and Windows DPAPI. |
| `BambuBar.Tests/` | Executable assertions, local TLS/MQTT broker and discovery tests; optional Docker-fleet integration. |
| `docker/`, `compose.yaml`, `build.ps1`, `scripts/build.sh` | Container toolchain, isolated emulator network, build/test/preview/publish entry points. |
| `installer/` | Per-user NSIS installation, shortcuts, running-instance check and uninstall cleanup. |
| `scripts/export-models.py` | Exports five compressed mesh families from the existing Blender generator. Generated meshes are committed so ordinary builds do not regenerate artwork. |
| `../.github/workflows/windows.yml` | Docker build/tests/package followed by native Windows installation and runtime smoke checks. |

The core reuses `Resources/Certificates`, `Resources/HMS`, `Resources/filaments.json`
and `Resources/Localizable.xcstrings`. The desktop reuses printer render assets and
geometry from `artwork/generate_printers.py`; it renders exported triangles through
Skia without a browser. The Windows target includes WinRT toast support; the plain
.NET target supports Linux/Xvfb preview checks.

Full printer reports replace report state; partial updates preserve omitted fields.
AMS and light arrays merge by identifier, while explicit empty arrays clear their
entries. Session state distinguishes connection/authentication/certificate failures
from missing reports and command refusals/timeouts. Certificate identity checks and
manual serial lookup happen before MQTT credentials are sent. Discovery never
receives an access code and cannot change a saved printer's IP without TLS validation.

The only shared simulator change is an opt-in `SSDP_RESOLVE_HOST=1`: advertisements
can contain the fleet container IP for tests. Existing macOS simulator behavior is
unchanged when the variable is absent. This fork runs only the Windows workflow;
the macOS source and local build scripts remain available without macOS CI builds.

## Feature parity

| Feature | Windows implementation |
| --- | --- |
| Multiple printers and primary printer | Implemented, including fleet page and tray picker. |
| LAN discovery / DHCP changes | Implemented with UDP advertisements/search, expiry, deduplication and TLS identity checks. |
| Manual registration | IP and access code; optional serial is read over verified TLS. Edit/delete and protected code reuse are supported. |
| Printing status | Job, layers, progress, translated stages/pause reasons, remaining time and stable finish estimate. Missing data remains unknown. |
| Telemetry | Model-dependent temperatures, dual nozzles, fans, door, Wi-Fi, storage, nozzle metadata and firmware-update availability. |
| AMS / external spool | Materials, brands, colors/multicolor, humidity/drying, nozzle ranges and grams when reported by the printer. No invented external-spool weight. |
| HMS / print errors | Localized messages, severity, codes and wiki link; cancellations distinguished from faults. |
| Controls | Pause/resume/stop, speed and light, firmware locks, stop confirmation, refusal/acknowledgement timeout reporting. |
| Menu bar equivalent | Windows tray icon with progress/tooltip and compact quick panel. Windows does not offer macOS-style persistent text beside the tray icon. |
| Transparency / rounded corners | Acrylic with supported fallbacks and native HWND clipping for the quick panel. Actual blur depends on Windows compositor settings. |
| Interactive 3D | Five original printer families, orbit/zoom/reset, filament colors, light and illustrative print/bed/toolhead motion. No live G-code reconstruction. |
| Notifications | Native Windows toasts for print transitions/errors with preferences, deduplication and printer activation. Disabled in demo/emulator. |
| Keyboard shortcuts | Navigation, refresh, light, pause/resume and stop in main/quick panels; camera keys in the focused 3D view. See the usage guide. |
| Localization | All 18 source-catalogue languages, positional placeholders and plural selection; HMS fallback follows available catalogues. |
| Credential storage | Per-user Windows DPAPI rather than macOS Keychain; settings under `%LOCALAPPDATA%\Bambu Bar`. |
| Slicer settings/code import | Not ported. Enter the LAN access code manually. |
| Last-print history / widgets | Not ported. |
| Open at login | No automatic registration; `--tray` is available for user-managed startup. |
| App update checks | Not ported. Firmware availability shown in telemetry is distinct from updating this app. |
| Camera | Not implemented. |
| Distribution/signing | Self-contained win-x64 installer and ZIP; unsigned. No automatic release publication. |

## Validation record

During implementation, Docker validation passed **28 core scenarios**, **30 scenarios
with fleet integration**, and the demo UI smoke checks. Checks cover parser/state
merging, TLS identity and credential ordering, authentication, reconnection, missing
reports, commands, discovery, telemetry, stages, AMS, errors, notifications, localization
and time projection. UI checks exercise page rendering, quick-panel controls and
placement, keyboard actions and all five 3D families, including camera persistence.
Installer/ZIP checksums and ZIP integrity were verified; the package includes the
.NET runtime, localization resources and Windows notification dependencies.

These results came from Linux containers and simulated printers. They do **not**
establish physical-printer compatibility, native toast banner/click behavior, real
Acrylic rendering, or Windows 10/11 installation success. The native Windows CI
job is configured to check installation, DPAPI, notification APIs, UI smoke rendering
and uninstall; its result must be read from the fork's Actions run rather than inferred
from a successful Docker build. The manual checklist in [README.md](README.md#validation)
covers Windows 10/11, 100%/150% scaling and physical printers.

Build artifacts, screenshots, local settings, NuGet caches and emulator-generated
private keys are excluded from source control. Dependencies are pinned in project
files and lockfiles; the .NET SDK image is digest-pinned. Debian OS tools are installed
from the image's configured apt repositories, so the toolchain is not fully hermetic.
