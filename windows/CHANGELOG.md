# Windows changelog

## Unreleased

- Publish GitHub Releases for matching `v*` version tags after Docker and native
  Windows checks, with installer, portable ZIP and checksums.

- Create a private runtime directory for each Docker build/test invocation so .NET
  named mutexes work with arbitrary host UIDs, including GitHub Actions UID 1001.
- Remove the upstream macOS CI workflow so this fork builds and tests Windows only.
  Keep the macOS source and local build scripts.

## 1.3.1 — initial Windows port

Versioned from upstream Bambu Bar 1.3.1; this is the first Windows implementation.

- Add .NET 10/Avalonia desktop app with printer, filament, fleet and settings pages,
  shared catalogue localization, light/dark themes and translucent surfaces.
- Add direct TLS MQTT sessions, strict Bambu certificate/serial checks, incremental
  report merging, reconnection, command confirmation and firmware-lock handling.
- Add local network discovery, verified DHCP address changes, manual IP registration,
  multiple printers, a primary printer and DPAPI-protected access codes.
- Add a progress tray icon and compact panel with controls, printer switching,
  DPI-aware placement and native rounded-corner clipping.
- Add interactive meshes from the original Blender artwork, orbit/zoom/reset,
  progressive print slices, filament colors and illustrative motion.
- Add configurable native notifications, keyboard shortcuts, telemetry, translated
  stages, HMS severity, AMS drying/humidity/multicolor and projected remaining time.
- Add Docker build/test/model-export tooling, local simulator integration, locked
  NuGet dependencies, self-contained installer/ZIP/checksums and Windows CI checks.
- Preserve macOS source/resources/workflow and original credits; add an opt-in
  container-address advertisement option to the shared fleet simulator.

See [implementation and validation limits](IMPLEMENTATION.md) and the
[build/install guide](README.md) before distributing the unsigned packages.
