# LazyNuGet

<div align="center">
  <img src=".github/logo.svg" alt="LazyNuGet Logo" width="600">
</div>

<div align="center">

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/v/LazyNuGet?logo=nuget&color=004880)](https://www.nuget.org/packages/LazyNuGet)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux%20|%20macOS%20|%20Windows-orange.svg)]()

</div>

**A terminal-based NuGet package manager inspired by lazygit.**

<div align="center">

### ⭐ If you find LazyNuGet useful, please consider giving it a star! ⭐

It helps others discover the project and motivates continued development.

[![GitHub stars](https://img.shields.io/github/stars/nickprotop/lazynuget?style=for-the-badge&logo=github&color=yellow)](https://github.com/nickprotop/lazynuget/stargazers)

</div>

LazyNuGet brings a fast, keyboard-driven interface to NuGet package management. Navigate your projects, view package details, check for updates, and search NuGet.org—all without leaving the terminal.

**Browse. Update. Search.**

![LazyNuGet Dashboard](.github/dashboard-overview.png)

**[View more screenshots](docs/SCREENSHOTS.md)**

## Quick Start

Get LazyNuGet running in seconds:

**Option 1: .NET Global Tool** (if you have .NET 9 installed)
```bash
dotnet tool install --global LazyNuGet
lazynuget
```

**Option 2: Self-contained binary** (no .NET required)
```bash
curl -fsSL https://raw.githubusercontent.com/nickprotop/lazynuget/main/install.sh | bash
lazynuget
```

That's it! Use arrow keys (or the mouse) to navigate, `Enter` to view package details, and `Ctrl+S` to search NuGet.org.

## Features

| | |
|---|---|
| 🎨 **Visual Interface** | Beautiful terminal UI, not just CLI commands |
| ⚡ **Smart Updates** | Auto-detect outdated packages across all projects |
| 🔍 **Package Search** | Browse and install from NuGet.org in-app |
| 🌳 **Dependencies** | Visualize project and package dependency trees |
| 📜 **History** | Track, retry, and undo NuGet operations |
| 🔒 **Security** | Per-package vulnerability details with severity and advisory links |
| 🏗️ **Multi-TF** | Full multi-target framework display (`net8.0 \| net9.0`) |
| 🔐 **Private Feeds** | Authenticated custom NuGet sources with stored credentials |
| 🗂️ **Solution Groups** | Projects grouped by `.sln` file in the sidebar |
| 🔄 **Migrate** | One-click migration from deprecated packages to their recommended replacements |
| 🧪 **Prerelease** | Prerelease version hints in package details |
| 📦 **CPM** | Full Central Package Management support — versions resolved from `Directory.Packages.props` |
| ⚙️ **Configuration** | Private feeds, custom sources, settings |

## Input

LazyNuGet is fully usable with both keyboard and mouse. Click to focus controls, scroll lists, and activate buttons. Keyboard shortcuts are available for every action and are the fastest way to navigate.

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `↑/↓` | Navigate lists |
| `Enter` | View package details / Select project |
| `Ctrl+O` | Open folder picker |
| `Ctrl+R` | Reload projects (clears cache) |
| `Ctrl+S` | Search NuGet.org |
| `Ctrl+D` | Dependency tree (project deps or package deps) |
| `Ctrl+H` | View operation history |
| `Ctrl+P` | Settings |
| `Ctrl+U` | Update package / Update all |
| `Ctrl+V` | Change package version |
| `Ctrl+X` | Remove package |
| `Ctrl+L` | Open log viewer |
| `Ctrl+↑/↓` | Scroll details panel |
| `Esc` | Go back / Close dialogs |
| `F1` | Package details — Overview tab |
| `F2` | Package details — Dependencies tab |
| `F3` | Package details — Versions tab |
| `F4` | Package details — What's New tab |
| `F5` | Package details — Security tab |

## Installation

### .NET Global Tool (Recommended for .NET developers)

If you have .NET 10.0 or later installed:

```bash
dotnet tool install --global LazyNuGet
```

**Advantages:**
- ✅ Single command installation
- ✅ Automatic updates with `dotnet tool update -g LazyNuGet`
- ✅ Works on all platforms (Windows, macOS, Linux)
- ✅ Lightweight (~5MB vs ~60MB for self-contained)

**Update:**
```bash
dotnet tool update --global LazyNuGet
```

**Uninstall:**
```bash
dotnet tool uninstall --global LazyNuGet
```

---

### Self-Contained Binaries (No .NET required)

#### Linux

Download and install the latest release:

```bash
curl -fsSL https://raw.githubusercontent.com/nickprotop/lazynuget/main/install.sh | bash
```

This automatically:
- Downloads the binary for your architecture (x64 or ARM64)
- Installs to `~/.local/bin/lazynuget`
- Adds `~/.local/bin` to your PATH

After installation, reload your shell:
```bash
source ~/.bashrc  # or ~/.zshrc
```

### macOS

```bash
curl -fsSL https://raw.githubusercontent.com/nickprotop/lazynuget/main/install.sh | bash
```

The installer detects macOS automatically and:
- Downloads the correct binary (Intel or Apple Silicon)
- Installs to `/usr/local/bin/` (or `~/.local/bin/` if not writable)
- Clears the Gatekeeper quarantine flag

> **Note:** If macOS still blocks the binary, run: `xattr -d com.apple.quarantine $(which lazynuget)`

### Windows

Open PowerShell and run:

```powershell
irm https://raw.githubusercontent.com/nickprotop/lazynuget/main/install.ps1 | iex
```

This installs to `%LOCALAPPDATA%\Programs\LazyNuGet\` and adds it to your user PATH.

> **Note:** If Windows SmartScreen blocks the binary, click "More info" then "Run anyway".

### Manual Install

Download the latest binary for your platform:
- [lazynuget-linux-x64](https://github.com/nickprotop/lazynuget/releases/latest) (Linux Intel/AMD)
- [lazynuget-linux-arm64](https://github.com/nickprotop/lazynuget/releases/latest) (Linux ARM)
- [lazynuget-osx-x64](https://github.com/nickprotop/lazynuget/releases/latest) (macOS Intel)
- [lazynuget-osx-arm64](https://github.com/nickprotop/lazynuget/releases/latest) (macOS Apple Silicon)
- [lazynuget-win-x64.exe](https://github.com/nickprotop/lazynuget/releases/latest) (Windows Intel/AMD)
- [lazynuget-win-arm64.exe](https://github.com/nickprotop/lazynuget/releases/latest) (Windows ARM)

Linux/macOS — make executable and move to PATH:
```bash
chmod +x lazynuget-linux-x64
sudo mv lazynuget-linux-x64 /usr/local/bin/lazynuget
```

### Build from Source

Requirements:
- .NET 10.0 SDK
- Terminal with Unicode support

```bash
git clone https://github.com/nickprotop/lazynuget.git
cd lazynuget
dotnet build -c Release
```

Run with:
```bash
dotnet run [path]
```

## Usage

```bash
# Manage packages in current directory
lazynuget

# Manage packages in specific directory
lazynuget /path/to/your/projects

# Show help
lazynuget --help
```

LazyNuGet will scan the directory for .csproj files and display all discovered projects and their packages.

## Troubleshooting

### Projects not found
LazyNuGet scans for `.csproj` files. Make sure you open the folder that contains your projects (or a parent folder). Folders named `bin`, `obj`, `.git`, and `node_modules` are skipped automatically.

### Packages show as up-to-date but shouldn't be
LazyNuGet caches package metadata during a session. Press `Ctrl+R` to force a full reload and clear the cache. Also confirm you have network access to NuGet.org.

### Outdated/vulnerable badges not appearing
Version checks run in the background after projects load. If the badge never appears, check that NuGet.org is reachable. A red indicator in the status bar usually means a source failed to connect.

### Private feed not showing packages
1. Confirm the feed URL is correct — open `Ctrl+P` → **Sources** tab and verify it is enabled.
2. If the feed requires authentication, make sure credentials are saved (edit the source and re-enter them).
3. On Linux/macOS credentials are stored as clear-text in `~/.nuget/NuGet/NuGet.Config`. On Windows they are encrypted with DPAPI.
4. If the feed was added outside LazyNuGet (e.g. via `dotnet nuget add source`), it will appear automatically — LazyNuGet reads the standard NuGet.config hierarchy.

### Authentication errors on a private feed
Re-enter credentials via `Ctrl+P` → select the source → press `E` to edit. If the feed uses a PAT (Personal Access Token), put the token in the **Password** field (username can be anything, e.g. `pat`).

### Terminal display looks broken (missing borders, garbled text)
LazyNuGet requires a terminal with **Unicode** and **256-colour** support. Recommended terminals: Windows Terminal, iTerm2, GNOME Terminal, Alacritty, Kitty. The basic Windows `cmd.exe` console is not supported — use Windows Terminal instead.

### macOS: "cannot be opened because the developer cannot be verified"
Run: `xattr -d com.apple.quarantine $(which lazynuget)`

---

## Uninstall

### Linux / macOS

```bash
lazynuget-uninstall
```

Or manually:
```bash
curl -fsSL https://raw.githubusercontent.com/nickprotop/lazynuget/main/uninstall.sh | bash
```

### Windows

```powershell
irm https://raw.githubusercontent.com/nickprotop/lazynuget/main/uninstall.ps1 | iex
```

Settings are stored in `~/.config/LazyNuGet/` (Linux), `~/Library/Application Support/LazyNuGet/` (macOS), or `%APPDATA%\LazyNuGet\` (Windows) and are not removed by the uninstaller.

## Built With

- [SharpConsoleUI](https://github.com/nickprotop/ConsoleEx) - A .NET library for building terminal user interfaces with responsive layouts and window management

## Author

**Nikolaos Protopapas**

- GitHub: [@nickprotop](https://github.com/nickprotop)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
