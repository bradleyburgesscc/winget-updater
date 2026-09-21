# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-09-21

### Added

- Initial release: an interactive TUI for selecting and applying `winget upgrade` updates.
- Arrow-key (or `j`/`k`) navigation, spacebar to select, enter to upgrade the selected
  packages.
- Each row shows the package Id, current version, and available version.
- `u`/`p` keys (or `--include-pinned` / `--no-include-unknown` flags) toggle inclusion
  of unknown-version and pinned packages, re-querying winget live.
- Always relaunches itself elevated once at startup so machine-scoped upgrades don't
  each prompt individually; continues unelevated if the UAC prompt is cancelled.
- Selected upgrades run sequentially with winget's native output streamed to the
  console, followed by a summary.
- Ships as a single self-contained NativeAOT executable (`winget-updater.exe`) with no
  runtime dependencies.
- `--selftest` flag runs embedded parser fixtures with no winget install or console
  required, covering package Ids containing spaces, missing "Available" values, a
  Source column, wide/CJK names, the "nothing to upgrade" shape, and winget's secondary
  "requires explicit targeting" table.

[0.1.0]: https://github.com/bradleyburgesscc/winget-updater/releases/tag/v0.1.0
