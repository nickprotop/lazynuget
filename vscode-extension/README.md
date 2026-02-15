<div align="center">
  <img src="https://raw.githubusercontent.com/nickprotop/lazynuget/main/.github/logo.svg" alt="LazyNuGet Logo" width="600">
</div>

<div align="center">

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/nickprotop/lazynuget/blob/main/LICENSE)
[![NuGet](https://img.shields.io/nuget/v/LazyNuGet?logo=nuget&color=004880)](https://www.nuget.org/packages/LazyNuGet)
[![VS Code](https://img.shields.io/visual-studio-marketplace/v/lazynuget.lazynuget?logo=visualstudiocode&color=007acc)](https://marketplace.visualstudio.com/items?itemName=lazynuget.lazynuget)

</div>

**A terminal-based NuGet package manager for VS Code, inspired by lazygit.**

LazyNuGet brings a fast, keyboard-and-mouse-driven interface to NuGet package management — right inside VS Code. Navigate your projects, view package details, check for updates, and search NuGet.org without leaving the editor.

**Browse. Update. Search.**

<!-- TODO: Replace with a screenshot/GIF of LazyNuGet running inside VS Code -->
<!-- Capture: Open VS Code with a .NET solution → Ctrl+Shift+N → screenshot the webview panel -->
![LazyNuGet in VS Code](https://raw.githubusercontent.com/nickprotop/lazynuget/main/.github/dashboard-overview.png)

## How It Works

This extension embeds the full LazyNuGet TUI inside a VS Code panel. It's not a stripped-down version — it's the real thing, with full mouse support, movable dialogs, and every feature of the standalone terminal app.

Under the hood, the extension spawns the LazyNuGet binary in a pseudo-terminal (PTY) and renders its output using [xterm.js](https://xtermjs.org/) in a Webview panel. Every keystroke and mouse event goes directly to the TUI with zero interception from VS Code.

## Getting Started

1. Install the extension from the VS Code Marketplace
2. Open a folder containing .NET projects
3. Press `Ctrl+Shift+N` (or `Cmd+Shift+N` on macOS)
4. LazyNuGet opens as an editor tab — navigate with keyboard or mouse

That's it. The binary is bundled with the extension — no separate installation needed.

> You can also open it via the Command Palette: `LazyNuGet: Open Package Manager`

## Features

| | |
|---|---|
| **Visual Interface** | Full terminal UI with mouse support, not just CLI commands |
| **Smart Updates** | Auto-detect outdated packages across all projects |
| **Package Search** | Browse and install from NuGet.org in-app |
| **Dependencies** | Visualize project and package dependency trees |
| **History** | Track and retry all NuGet operations |
| **Configuration** | Private feeds, custom sources, settings |

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `↑/↓` | Navigate lists |
| `Enter` | View package details / Select project |
| `Ctrl+O` | Open folder picker |
| `Ctrl+R` | Reload projects |
| `Ctrl+S` | Search NuGet.org |
| `Ctrl+D` | Dependency tree |
| `Ctrl+H` | View operation history |
| `Ctrl+P` | Settings |
| `Ctrl+U` | Update package / Update all |
| `Ctrl+V` | Change package version |
| `Ctrl+X` | Remove package |
| `Ctrl+L` | Open log viewer |
| `Ctrl+↑/↓` | Scroll details panel |
| `Esc` | Go back / Close dialogs |

Full mouse support is also available — click on projects, packages, buttons, and drag modal dialogs.

## Screenshots

### Dashboard Overview

The main dashboard shows your project tree on the left and package details on the right. Quickly see which packages are outdated and navigate with keyboard or mouse.

<!-- TODO: Replace with VS Code-specific screenshots showing the TUI inside the editor -->
![Dashboard Overview](https://raw.githubusercontent.com/nickprotop/lazynuget/main/docs/screenshot-1-dashboard.png)

### Package Details

Press Enter on any package to view detailed information fetched from NuGet.org, including description, available versions, and metadata.

![Package Details](https://raw.githubusercontent.com/nickprotop/lazynuget/main/docs/screenshot-2-package-details.png)

### NuGet.org Search

Search for new packages directly from NuGet.org with real-time results and metadata.

![NuGet Search](https://raw.githubusercontent.com/nickprotop/lazynuget/main/docs/screenshot-4-search.png)

### Operation History

View and manage your complete NuGet operation history with filtering and retry capabilities.

![Operation History](https://raw.githubusercontent.com/nickprotop/lazynuget/main/docs/screenshot-5-operation-history.png)

## Extension Settings

| Setting | Description | Default |
|---|---|---|
| `lazynuget.binaryPath` | Override path to the lazynuget binary | (uses bundled binary) |

## Requirements

- VS Code 1.85.0 or later
- A folder containing .NET projects (.csproj, .fsproj, .vbproj)

No .NET SDK is required — the LazyNuGet binary is self-contained and bundled with the extension.

## Standalone Usage

LazyNuGet also works as a standalone terminal application outside VS Code. See the [main repository](https://github.com/nickprotop/lazynuget) for installation options:

- **.NET Global Tool:** `dotnet tool install --global LazyNuGet`
- **Self-contained binary:** Available for Linux, macOS, and Windows

## Built With

- [SharpConsoleUI](https://github.com/nickprotop/ConsoleEx) — Terminal UI framework with responsive layouts and window management
- [xterm.js](https://xtermjs.org/) — Terminal rendering in the VS Code Webview
- [node-pty](https://github.com/nickterminal/node-pty) — Pseudo-terminal for full TUI support

## Author

**Nikolaos Protopapas** — [@nickprotop](https://github.com/nickprotop)

## License

MIT — see the [LICENSE](https://github.com/nickprotop/lazynuget/blob/main/LICENSE) file for details.
