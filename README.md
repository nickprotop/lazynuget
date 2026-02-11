# LazyNuGet

<div align="center">
  <img src=".github/logo.svg" alt="LazyNuGet Logo" width="600">
</div>

<div align="center">

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux-orange.svg)]()

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

Get LazyNuGet running in 3 steps:

```bash
# 1. Install LazyNuGet
curl -fsSL https://raw.githubusercontent.com/nickprotop/lazynuget/main/install.sh | bash

# 2. Navigate to your .NET project(s)
cd ~/projects

# 3. Run it
lazynuget
```

That's it! Use arrow keys to navigate, `Enter` to view package details, and `Ctrl+S` to search NuGet.org.

## Features

- 📦 **Project Discovery** - Automatically finds all .csproj files in your directory tree
- 🔍 **Package Browser** - View installed packages with version information
- ⚠️ **Update Detection** - Identifies outdated packages with real-time NuGet.org checks
- 🔎 **NuGet.org Search** - Search and browse packages directly from the terminal
- 📜 **Operation History** - Track all NuGet operations with filtering and retry capabilities (Ctrl+H)
- 📊 **Rich Package Metadata** - View authors, license, tags, target frameworks, vulnerabilities, and more
- ⌨️ **Keyboard-Driven** - Navigate everything with keyboard shortcuts (lazygit-style)
- 🎨 **Clean TUI** - Beautiful terminal interface with syntax highlighting
- 📊 **Dashboard Stats** - See package counts and outdated packages at a glance
- 📁 **Folder Picker** - Native folder navigation with Ctrl+O

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `↑/↓` | Navigate lists |
| `Enter` | View package details / Select project |
| `Ctrl+O` | Open folder picker |
| `Ctrl+R` | Reload projects |
| `Ctrl+S` | Search NuGet.org |
| `Ctrl+H` | View operation history |
| `Ctrl+U` | Update package / Update all |
| `Ctrl+V` | Change package version |
| `Ctrl+X` | Remove package |
| `Esc` | Go back / Close dialogs |
| `Alt+F` | File menu |

## Installation

### Quick Install (Recommended)

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

### Manual Install

Download the latest binary for your architecture:
- [lazynuget-linux-x64](https://github.com/nickprotop/lazynuget/releases/latest) (Intel/AMD)
- [lazynuget-linux-arm64](https://github.com/nickprotop/lazynuget/releases/latest) (ARM/Raspberry Pi)

Make executable and move to PATH:
```bash
chmod +x lazynuget-linux-x64
sudo mv lazynuget-linux-x64 /usr/local/bin/lazynuget
```

### Build from Source

Requirements:
- .NET 9.0 SDK
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

## Uninstall

To remove LazyNuGet from your system:

```bash
lazynuget-uninstall
```

Or manually:
```bash
curl -fsSL https://raw.githubusercontent.com/nickprotop/lazynuget/main/uninstall.sh | bash
```

This removes the binary from `~/.local/bin/lazynuget`. LazyNuGet does not create any configuration files.

## Built With

- [SharpConsoleUI](https://github.com/nickprotop/ConsoleEx) - A .NET library for building terminal user interfaces with responsive layouts and window management

## Author

**Nikolaos Protopapas**

- GitHub: [@nickprotop](https://github.com/nickprotop)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
