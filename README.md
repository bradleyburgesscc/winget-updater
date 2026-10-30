# winget-updater

[![CI](https://github.com/bradleyburgesscc/winget-updater/actions/workflows/ci.yml/badge.svg)](https://github.com/bradleyburgesscc/winget-updater/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/bradleyburgesscc/winget-updater)](https://github.com/bradleyburgesscc/winget-updater/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A small interactive TUI for `winget upgrade`. It lists your pending package upgrades,
lets you pick exactly which ones you want with the keyboard, and upgrades only those.

```
  winget upgrade — 2 of 4 selected                    [unknown: on  pinned: off]

  [x] Docker.DockerDesktop                4.48.0       -> 4.51.2        Docker Desktop
  [ ] Microsoft.PowerToys                 0.96.1       -> 0.97.0        PowerToys (Preview)
> [x] Git.Git                             2.51.0       -> 2.52.1        Git
  [ ] Microsoft.SQLServer.2025.Developer  Unknown      -> 17.0.1000.7   Microsoft SQL Server 2025

  ↑/↓ move   space select   a all   n none   u unknown   p pinned   r refresh
  enter upgrade   q quit
```

## Installation

Download `winget-updater.exe` (or the zip) from the [latest release](https://github.com/bradleyburgesscc/winget-updater/releases/latest) — it's a single self-contained file, no installer or runtime needed.

## Usage

```
winget-updater.exe [options]
```

| Option | Effect |
|---|---|
| `--no-elevate` | Don't relaunch elevated; run with current privileges. |
| `--include-pinned` | Also list packages that have a non-blocking pin. |
| `--include-unknown` | Also list packages whose installed version winget can't determine. |
| `--selftest` | Run the internal parser self-tests and exit — no winget or admin needed. |
| `-h`, `--help` | Show usage. |

By default the tool relaunches itself elevated (one UAC prompt) so machine-scoped
upgrades don't each trigger their own prompt. If you cancel the UAC prompt it continues
unelevated instead of exiting.

### Controls

| Key | Action |
|---|---|
| `↑`/`↓`, `k`/`j`, PgUp/PgDn, Home/End | Move the cursor |
| `Space` | Toggle the highlighted package |
| `a` / `n` | Select all / select none |
| `u` | Toggle "include unknown-version packages" and re-query |
| `p` | Toggle "include pinned packages" and re-query |
| `r` | Re-query winget without changing filters |
| `Enter` | Upgrade the selected packages |
| `q` / `Esc` | Quit without changing anything |

Upgrades run one at a time after the TUI closes, with winget's own native output visible
(so real download progress and any installer prompts show up as normal), followed by a
summary.

## Building

Requires the .NET 10 SDK and the MSVC "Desktop development with C++" build tools
(NativeAOT's linker). No NuGet packages are used.

```powershell
.\build.ps1
```

produces a single self-contained `dist\winget-updater.exe` with no other runtime
dependencies.

For local iteration, `dotnet build` / `dotnet run` work as usual (JIT, not AOT).

## How the parsing works

`winget upgrade` has no JSON output in the installed winget version, so this tool parses
its fixed-width text table. Column boundaries are derived from the **position** of each
header word (not its text, which is localized) and each row is sliced by *display
column* rather than character index, since a wide character earlier in a row shifts
every later column. See `UpgradeTableParser.cs` for details, and run
`winget-updater.exe --selftest` to exercise the parser against a set of fixtures
(embedded package IDs containing spaces, missing "Available" values, a Source column,
wide/CJK names, the "nothing to upgrade" shape, and the secondary "requires explicit
targeting" table winget sometimes prints).

## Out of scope

This tool only drives `winget upgrade` for already-installed packages. It does not
install new packages, manage sources, or read/write any config file.

## Releasing

Pushing a tag matching `v*.*.*` (e.g. `v0.1.0`) triggers the
[release workflow](.github/workflows/release.yml), which publishes the NativeAOT exe,
runs `--selftest` against it as a release gate, and attaches it (and a zip) to a new
GitHub release. See [CHANGELOG.md](CHANGELOG.md) for what's in each version.

## License

[MIT](LICENSE)
