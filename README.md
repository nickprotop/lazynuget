# LazyNuGet

A terminal-based NuGet package manager for .NET projects, inspired by lazygit. Built with [ConsoleEx](https://github.com/your-username/ConsoleEx) for a fast, keyboard-driven interface.

## Features

- 📦 View all .NET projects in a folder tree
- 🔍 Browse installed packages across multiple projects
- 🎨 Beautiful TUI with AgentStudio-style aesthetics
- ⌨️ Keyboard-driven navigation (lazygit-style)
- 📊 Project dashboard with package statistics
- 🔄 Context-switching between Projects → Packages views
- 📁 Native folder picker for easy navigation

## Prerequisites

- .NET 9.0 SDK or later
- Terminal with Unicode support (for box-drawing characters)

## Building

```bash
dotnet build
```

## Running

### Option 1: Use current directory

```bash
dotnet run
```

### Option 2: Specify a project folder

```bash
dotnet run /path/to/your/projects
```

### Option 3: Run the compiled executable

```bash
./bin/Debug/net9.0/LazyNuGet /path/to/your/projects
```

## Keyboard Shortcuts

### Global

- **Ctrl+O**: Open folder picker
- **Ctrl+R**: Reload projects
- **Ctrl+S**: Search NuGet.org (Phase 4 - coming soon)
- **Esc**: Navigate back / Close dialogs
- **↑/↓**: Navigate lists
- **Alt+F**: Open File menu

### Projects View

- **Enter**: View project packages
- **Ctrl+U**: Update all outdated packages (Phase 4)
- **Ctrl+R**: Restore packages (Phase 4)

### Packages View

- **Enter**: View package details
- **Esc**: Back to projects
- **Ctrl+U**: Update selected package (Phase 4)
- **Ctrl+X**: Remove selected package (Phase 4)

## Implementation Status

### ✅ Phase 1: Foundation (COMPLETED)
- Project structure and build system
- 2-panel UI layout with context-switching
- Color scheme and styling
- Top menu with File operations
- Folder picker integration
- Keyboard navigation framework

### ✅ Phase 2: Project Discovery & Dashboard (COMPLETED)
- Project discovery service (finds .csproj files recursively)
- Project parser service (extracts PackageReferences from XML)
- Project dashboard with stats cards
- Package list view
- Breadcrumb navigation
- Enhanced package details display

### ✅ Phase 3: Package List & Details (COMPLETED)
- NuGet.org API integration (v3 API)
- Real-time package version checking
- Package description and metadata
- Version history display
- Async package details loading
- Outdated package detection

### 🚧 Phase 4: Package Operations (PLANNED)
- Search NuGet.org modal
- Install packages
- Update packages
- Remove packages
- Confirmation dialogs
- Error handling

### 🚧 Phase 5: Polish & Refinements (PLANNED)
- Background refresh for outdated packages
- Configuration persistence
- Loading indicators
- Update all functionality
- Cross-platform testing

## Architecture

```
LazyNuGet/
├── Models/              # Data models
├── Services/            # Business logic
│   ├── ProjectDiscoveryService.cs
│   ├── ProjectParserService.cs
│   ├── NuGetClientService.cs (Phase 3)
│   └── DotNetCliService.cs (Phase 4)
└── UI/
    ├── Components/      # Reusable UI builders
    ├── Modals/          # Dialog windows (Phase 4)
    └── Utilities/       # Color schemes, helpers
```

## Current Limitations

- **No package operations yet**: Install/update/remove coming in Phase 4
- **No search**: NuGet.org search coming in Phase 4
- **No persistence**: Settings not saved between sessions (Phase 5)

## Development

### Conditional ConsoleEx Reference

The project uses a conditional reference to ConsoleEx:
- If `../ConsoleEx/SharpConsoleUI/SharpConsoleUI.csproj` exists → uses local project
- Otherwise → uses NuGet package `SharpConsoleUI`

### Testing

A test project has been created at `/tmp/lazynuget-test/TestApp` with intentionally outdated packages.

To test LazyNuGet with the demo project:

```bash
# Run LazyNuGet pointing to the test project
dotnet run /tmp/lazynuget-test

# You should see:
# - 1 project (TestApp)
# - 3 packages (Spectre.Console, Newtonsoft.Json, Serilog)
# - Some packages marked as outdated (yellow ⚠ indicators)
# - Press Enter on TestApp to view its packages
# - Select a package to see details from NuGet.org
```

To create your own test projects:

```bash
# Create a test .NET project
mkdir test-projects
cd test-projects
dotnet new console -n TestApp
cd TestApp
dotnet add package Spectre.Console
dotnet add package Newtonsoft.Json --version 13.0.1
cd ../..

# Run LazyNuGet
dotnet run test-projects
```

## License

MIT

## Contributing

Contributions welcome! This is currently in active development.

## Acknowledgments

- Built with [ConsoleEx/SharpConsoleUI](https://github.com/your-username/ConsoleEx)
- Inspired by [lazygit](https://github.com/jesseduffield/lazygit)
- Uses [Spectre.Console](https://spectreconsole.net/) for markup rendering
