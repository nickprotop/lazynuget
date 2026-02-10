# LazyNuGet - TUI NuGet Package Manager Implementation Plan

## Context

Building a terminal-based NuGet package manager for .NET projects using the ConsoleEx library. The goal is to provide a fast, keyboard-driven interface for managing NuGet packages across multiple projects - inspired by tools like `lazygit`. The user wants AgentStudio/ConsoleTop aesthetics with a 3-panel layout, command-line folder selection, and native file dialogs for folder navigation.

**Why this is needed:**
- Simplify NuGet package management in terminal workflows
- Provide visual overview of packages across multiple projects
- Keyboard-driven efficiency for developers who prefer TUI tools
- Cross-platform solution for .NET development

## Project Structure

```
LazyNuGet/
├── LazyNuGet.csproj              # Conditional ConsoleEx reference
├── Program.cs                     # Entry point with CLI args handling
├── LazyNuGetWindow.cs            # Main window with 3-panel layout
├── Models/
│   ├── ProjectInfo.cs            # Project metadata and packages
│   ├── PackageReference.cs       # Installed package info
│   ├── NuGetPackage.cs          # NuGet.org package data
│   └── OperationResult.cs       # CLI operation results
├── Services/
│   ├── ProjectDiscoveryService.cs   # Find .csproj files recursively
│   ├── ProjectParserService.cs      # Parse PackageReference from XML
│   ├── NuGetClientService.cs        # Search/fetch from NuGet.org API
│   ├── DotNetCliService.cs          # Execute dotnet add/remove/restore
│   └── ConfigurationService.cs      # Persist settings (last folder, etc.)
└── UI/
    ├── Modals/
    │   ├── SearchPackageModal.cs     # Search NuGet.org
    │   ├── ConfirmationModal.cs      # Confirm operations
    │   └── ErrorModal.cs             # Show error details
    ├── Components/
    │   ├── ProjectDashboardBuilder.cs   # Build dashboard view for projects
    │   └── PackageDetailsBuilder.cs     # Build details view for packages
    └── Utilities/
        └── ColorScheme.cs            # Centralized color definitions
```

## Implementation Approach

### 1. ConsoleEx Integration

**Project File (LazyNuGet.csproj):**
```xml
<ItemGroup Condition="Exists('../ConsoleEx/SharpConsoleUI/SharpConsoleUI.csproj')">
  <ProjectReference Include="../ConsoleEx/SharpConsoleUI/SharpConsoleUI.csproj" />
</ItemGroup>
<ItemGroup Condition="!Exists('../ConsoleEx/SharpConsoleUI/SharpConsoleUI.csproj')">
  <PackageReference Include="SharpConsoleUI" Version="2.0.0" />
</ItemGroup>
```

### 2. UI Layout (2-Panel Context-Switching Design)

**Pattern Reference:** `/home/nick/source/ConsoleEx/Examples/AgentStudio/AgentStudioWindow.cs` (lines 114-161)

**Navigation Model (lazygit-style):**
- Left panel: Context-switching list (Projects → Packages → Search results)
- Right panel: Details (Dashboard for projects, info for packages)
- Enter: Drill down / navigate forward
- Esc: Go back to previous context

```
Window (fullscreen, borderless, maximized)
├── MenuControl (StickyTop)
│   └── File > Open Folder, Reload, Exit
├── RuleControl (separator)
├── TopStatusBar (StickyTop, HorizontalGrid)
│   ├── Left: "Folder: /path/to/projects" or "MyApp.Web › Packages" (breadcrumb)
│   └── Right: "3 projects | 42 packages | 15:30:42"
├── MainGrid (HorizontalGrid, VerticalAlignment.Fill)
│   ├── Column 0 (width 40): Context List Panel
│   │   ├── Header: "[cyan1 bold]Projects[/]" (or "Packages" or "Search Results")
│   │   └── ListControl (content changes based on context)
│   ├── Column 1 (width 1): Spacing
│   └── Column 2 (flex, Grey19 bg): Details/Dashboard Panel
│       ├── Header: "[cyan1 bold]Details[/]" (or "Dashboard")
│       └── ScrollablePanelControl (dashboard cards or package details)
├── RuleControl (separator)
└── BottomHelpBar (StickyBottom)
    └── Context-aware hints: "Enter:Open  Esc:Back  Ctrl+S:Search  Ctrl+I:Install"
```

**View States:**

**State 1: Projects View**
- Left: Project list with stats (packages count, outdated count)
- Right: Project dashboard with stats cards, recent updates, packages needing attention

**State 2: Packages View** (after Enter on project)
- Left: Package list for selected project (with version indicators)
- Right: Selected package details from NuGet.org API

**State 3: Search View** (after Ctrl+S)
- Left: Search results from NuGet.org
- Right: Selected package details with "Install to:" project selector

**Color Scheme (AgentStudio aesthetic):**
- Background: Color.Grey11
- Status bars: Color.Grey15
- Sidebar: Color.Grey19
- Rules: Color.Grey23
- Accent: Color.Cyan1
- Success: Color.Green (up-to-date packages)
- Warning: Color.Yellow (outdated packages)
- Error: Color.Red (conflicts/vulnerabilities)

**Detailed Mockups:**

**State 1: Projects View with Dashboard**
```
┌─ File ──────────────────────────────────────────────────────────────┐
│ Open Folder | Reload | Exit                                         │
├──────────────────────────────────────────────────────────────────────┤
│ /home/user/projects               3 projects | 42 packages | 15:30  │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│ ┌─ Projects ────────────────────┬─ Dashboard ─────────────────────┐ │
│ │                               │                                 │ │
│ │ ▶ MyApp.Web                   │ Project: MyApp.Web              │ │
│ │   📦 12 packages · net9.0     │ Path: ~/projects/MyApp.Web      │ │
│ │   ⚠ 3 outdated                │ Framework: net9.0               │ │
│ │                               │                                 │ │
│ │   MyApp.Core                  │ ┌─────────┬─────────┬─────────┐ │ │
│ │   📦 8 packages · net9.0      │ │ Total   │Outdated │  Vuln   │ │ │
│ │   ✓ All up-to-date            │ │         │         │         │ │ │
│ │                               │ │   12    │    3    │    0    │ │ │
│ │   MyApp.Tests                 │ │         │         │         │ │ │
│ │   📦 5 packages · net9.0      │ │ 📦      │ ⚠       │ ✓       │ │ │
│ │   ✓ All up-to-date            │ └─────────┴─────────┴─────────┘ │ │
│ │                               │                                 │ │
│ │                               │ Recently Updated:               │ │
│ │                               │ • Spectre.Console (2 days ago)  │ │
│ │                               │   0.49.0 → 0.49.1               │ │
│ │                               │ • Serilog (1 week ago)          │ │
│ │                               │   4.0.0 → 4.1.0                 │ │
│ │ [i] Press Enter to view       │                                 │ │
│ │     packages                  │ Needs Attention:                │ │
│ │                               │ ⚠ Newtonsoft.Json               │ │
│ │                               │   13.0.1 → 13.0.3 available     │ │
│ │                               │ ⚠ Dapper                        │ │
│ │                               │   2.0.0 → 2.1.35 available      │ │
│ │                               │ ⚠ xUnit                         │ │
│ │                               │   2.4.0 → 2.6.6 available       │ │
│ │                               │                                 │ │
│ │                               │ Quick Actions:                  │ │
│ │                               │ [Enter] View packages           │ │
│ │                               │ [Ctrl+U] Update all outdated    │ │
│ │                               │ [Ctrl+R] Restore packages       │ │
│ └───────────────────────────────┴─────────────────────────────────┘ │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│ Enter:View Packages  Ctrl+S:Search  Ctrl+U:Update All  ?:Help       │
└──────────────────────────────────────────────────────────────────────┘
```

**State 2: Packages View** (after Enter on MyApp.Web)
```
┌─ File ──────────────────────────────────────────────────────────────┐
│ Open Folder | Reload | Exit                                         │
├──────────────────────────────────────────────────────────────────────┤
│ MyApp.Web › Packages              3 projects | 42 packages | 15:30  │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│ ┌─ MyApp.Web › Packages ────────┬─ Details ───────────────────────┐ │
│ │                               │                                 │ │
│ │ ▶ Spectre.Console             │ Package: Spectre.Console        │ │
│ │   ✓ 0.49.1 (latest)           │ Installed: 0.49.1 (latest)      │ │
│ │                               │ Published: 2024-01-15           │ │
│ │   Newtonsoft.Json             │ Downloads: 142,547,893          │ │
│ │   ⚠ 13.0.1 → 13.0.3           │ License: MIT                    │ │
│ │                               │                                 │ │
│ │   Serilog                     │ Description:                    │ │
│ │   ✓ 4.1.0 (latest)            │ A .NET library that makes it    │ │
│ │                               │ easier and more enjoyable to    │ │
│ │   Microsoft.Extensions...     │ create beautiful console        │ │
│ │   ✓ 9.0.0 (latest)            │ applications.                   │ │
│ │                               │                                 │ │
│ │   Dapper                      │ Project URL:                    │ │
│ │   ⚠ 2.0.0 → 2.1.35            │ https://spectreconsole.net      │ │
│ │                               │                                 │ │
│ │   xUnit                       │ Available Versions:             │ │
│ │   ⚠ 2.4.0 → 2.6.6             │ 0.49.1 (latest) ◄ installed     │ │
│ │                               │ 0.49.0                          │ │
│ │ [i] ✓ up-to-date              │ 0.48.0                          │ │
│ │     ⚠ outdated                │ 0.47.0                          │ │
│ │                               │ ...                             │ │
│ │ [i] Press Esc to go back      │                                 │ │
│ │     to projects               │ Actions:                        │ │
│ │                               │ [Ctrl+U] Update to latest       │ │
│ │                               │ [Ctrl+X] Remove package         │ │
│ │                               │ [Enter] More details            │ │
│ └───────────────────────────────┴─────────────────────────────────┘ │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│ Esc:Back  Ctrl+U:Update  Ctrl+X:Remove  Ctrl+S:Search  ?:Help       │
└──────────────────────────────────────────────────────────────────────┘
```

**State 3: Search View** (after Ctrl+S)
```
┌─ File ──────────────────────────────────────────────────────────────┐
│ Open Folder | Reload | Exit                                         │
├──────────────────────────────────────────────────────────────────────┤
│ Search › json                     3 projects | 42 packages | 15:30  │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│ ┌─ Search NuGet.org ────────────┬─ Details ───────────────────────┐ │
│ │                               │                                 │ │
│ │ 🔍 json_                      │ Package: Newtonsoft.Json        │ │
│ │                               │ Latest: 13.0.3                  │ │
│ │ Results (142):                │ Published: 2023-03-12           │ │
│ │                               │ Downloads: 2,547,893,421        │ │
│ │ ▶ Newtonsoft.Json             │ License: MIT                    │ │
│ │   13.0.3                      │                                 │ │
│ │   ★★★★★ 2.5B downloads        │ Description:                    │ │
│ │                               │ Json.NET is a popular high-     │ │
│ │   System.Text.Json            │ performance JSON framework      │ │
│ │   9.0.0                       │ for .NET                        │ │
│ │   ★★★★☆ 847M downloads        │                                 │ │
│ │                               │ Compatible Frameworks:          │ │
│ │   JsonSubTypes                │ • .NET 9.0                      │ │
│ │   2.0.1                       │ • .NET 8.0                      │ │
│ │   ★★★☆☆ 12M downloads         │ • .NET Standard 2.0             │ │
│ │                               │ • .NET Framework 4.5+           │ │
│ │   RestSharp                   │                                 │ │
│ │   111.4.1                     │ ┌─ Install to ────────────────┐ │ │
│ │   ★★★★☆ 247M downloads        │ │ ▶ MyApp.Web                 │ │ │
│ │                               │ │   MyApp.Core                │ │ │
│ │ [i] Type to search            │ │   MyApp.Tests               │ │ │
│ │     Enter to select project   │ └─────────────────────────────┘ │ │
│ │     Esc to cancel             │                                 │ │
│ │                               │ [Enter] Install to selected     │ │
│ └───────────────────────────────┴─────────────────────────────────┘ │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│ Esc:Cancel  Enter:Install  ↑↓:Navigate  Tab:Switch Panel  ?:Help    │
└──────────────────────────────────────────────────────────────────────┘
```

### 3. Command-Line & Folder Selection

**Entry Point (Program.cs):**
```csharp
static async Task<int> Main(string[] args)
{
    // Use CLI arg if provided, otherwise use current directory
    string folderPath = args.Length > 0 ? args[0] : Environment.CurrentDirectory;

    var windowSystem = new ConsoleWindowSystem(
        new NetConsoleDriver(RenderMode.Buffer),
        options: new ConsoleWindowSystemOptions(
            StatusBarOptions: new StatusBarOptions(
                ShowTaskBar: false,
                ShowBottomStatus: false
            )
        ));

    using var mainWindow = new LazyNuGetWindow(windowSystem, folderPath);
    mainWindow.Show();
    await Task.Run(() => windowSystem.Run());
}
```

**File Dialog Integration:**
- Use `FileDialogs.ShowFolderPickerAsync(windowSystem, startPath, parentWindow)` from ConsoleEx
- Reference: `/home/nick/source/ConsoleEx/SharpConsoleUI/Dialogs/FileDialogs.cs`
- Trigger from File > Open Folder menu or Ctrl+O shortcut
- Default behavior: Uses current directory (Environment.CurrentDirectory) when no CLI argument provided

### 4. Data Flow

**Project Discovery Flow:**
1. User opens folder (CLI arg or file dialog)
2. `ProjectDiscoveryService.DiscoverProjectsAsync(folder)` - find .csproj/.fsproj recursively
3. `ProjectParserService.ParseProject(path)` - parse XML with System.Xml.Linq
4. Extract `<PackageReference Include="..." Version="..." />` elements
5. Populate left panel ListControl with project items

**Project Dashboard Flow:**
1. User selects project in left panel
2. Build dashboard view in right panel with:
   - Stats cards (total packages, outdated count, vulnerable count)
   - Recently updated packages list (from last modified dates)
   - Packages needing attention (outdated packages)
   - Quick action buttons
3. Background: Check for outdated versions via NuGet API
4. Update dashboard cards dynamically as data arrives

**Package List Flow:**
1. User presses Enter on project → Switch left panel to package list
2. Update breadcrumb: "MyApp.Web › Packages"
3. Display packages with color indicators (green/yellow/red)
4. User selects package → Load details in right panel
5. `NuGetClientService.GetPackageDetailsAsync(packageId)` - call NuGet v3 API
6. Update right panel with formatted package details
7. Background thread checks for outdated versions (every 30s)

**Package Operations Flow:**
1. User presses Ctrl+S → show SearchPackageModal
2. User searches NuGet.org → display results in modal ListControl
3. User selects package → choose version
4. Show ConfirmationModal → "Install X version Y to ProjectZ?"
5. Execute `dotnet add package` via DotNetCliService
6. Reload project XML and update UI
7. Show success message in status bar or ErrorModal on failure

### 5. NuGet Integration

**NuGet V3 API:**
- Search: `https://azuresearch-usnc.nuget.org/query?q={query}&take=20`
- Package details: `https://api.nuget.org/v3/registration5-semver1/{packageId}/index.json`
- Use HttpClient with JSON deserialization

**DotNet CLI Commands:**
```bash
dotnet add <PROJECT> package <PACKAGE_ID> [--version <VERSION>]
dotnet remove <PROJECT> package <PACKAGE_ID>
dotnet restore <PROJECT>
```

**Implementation:**
- Use `Process.Start()` with RedirectStandardOutput/Error
- Capture stdout/stderr for user feedback
- Return structured `OperationResult(Success, Message, ErrorDetails, ExitCode)`

### 6. Async Patterns

**Window Refresh Thread:**
```csharp
WindowBuilder.WithAsyncWindowThread(RefreshThreadAsync)

private async Task RefreshThreadAsync(Window window, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        // Update clock every 1s
        _topStatusRight?.SetContent(new List<string> {
            $"[grey70]{DateTime.Now:HH:mm:ss}[/]"
        });

        // Check outdated packages every 30s
        await Task.Delay(1000, ct);
    }
}
```

**Reference:** `/home/nick/source/ConsoleEx/Examples/AgentStudio/AgentStudioWindow.cs` (lines 214-248)

### 7. Event Handling

**Keyboard Shortcuts:**
```csharp
window.KeyPressed += (sender, e) =>
{
    if (e.AlreadyHandled) { e.Handled = true; return; }

    if (e.KeyInfo.Key == ConsoleKey.O && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
    {
        _ = PromptForFolderAsync();
        e.Handled = true;
    }
    else if (e.KeyInfo.Key == ConsoleKey.S && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
    {
        _ = ShowSearchModalAsync();
        e.Handled = true;
    }
    // Ctrl+I, Ctrl+U, Ctrl+R for install/update/remove
};
```

**List Selection:**
```csharp
_projectList.SelectedIndexChanged += (s, e) =>
{
    if (_projectList.SelectedItem?.Tag is ProjectInfo project)
    {
        UpdatePackageList(project);
    }
};
```

**Reference:** `/home/nick/source/ConsoleEx/Examples/AgentStudio/AgentStudioWindow.cs` (lines 254-310)

### 8. Modal Dialogs

**Search Modal Pattern:**
- Create modal window with `.AsModal()` and `.Centered()`
- ListControl for results
- TaskCompletionSource for async result handling
- Escape to cancel, Enter to select

**Reference:** `/home/nick/source/ConsoleEx/Examples/AgentStudio/Modals/CommandPaletteModal.cs`

**Confirmation Modal:**
- Yes/No buttons using HorizontalGrid
- Keyboard shortcuts: Y/Enter for yes, N/Escape for no
- Return bool via TaskCompletionSource

### 9. Dashboard Component Implementation

**ProjectDashboardBuilder Pattern:**

The dashboard is built dynamically using MarkupControl with formatted text. Create static cards layout:

```csharp
public static class ProjectDashboardBuilder
{
    public static List<string> BuildDashboard(ProjectInfo project,
                                              List<PackageReference> outdatedPackages)
    {
        var lines = new List<string>();

        // Project header
        lines.Add($"[cyan1 bold]Project: {Markup.Escape(project.Name)}[/]");
        lines.Add($"[grey70]Path: {Markup.Escape(project.FilePath)}[/]");
        lines.Add($"[grey70]Framework: {project.TargetFramework}[/]");
        lines.Add("");

        // Stats cards (3 columns using fixed-width formatting)
        var total = project.Packages.Count;
        var outdated = outdatedPackages.Count;
        var vulnerable = project.Packages.Count(p => p.HasVulnerability);

        lines.Add("┌─────────┬─────────┬─────────┐");
        lines.Add("│ Total   │Outdated │  Vuln   │");
        lines.Add("│         │         │         │");
        lines.Add($"│  [cyan1]{total,3}[/]    │  [yellow]{outdated,3}[/]    │  [red]{vulnerable,3}[/]    │");
        lines.Add("│         │         │         │");
        lines.Add("│ 📦      │ ⚠       │ ✓       │");
        lines.Add("└─────────┴─────────┴─────────┘");
        lines.Add("");

        // Recently Updated section
        lines.Add("[grey70 bold]Recently Updated:[/]");
        var recentUpdates = GetRecentUpdates(project.Packages);
        if (recentUpdates.Any())
        {
            foreach (var pkg in recentUpdates.Take(3))
            {
                lines.Add($"[grey70]• {Markup.Escape(pkg.Id)} ({pkg.TimeSinceUpdate})[/]");
                lines.Add($"  [grey50]{pkg.OldVersion} → {pkg.NewVersion}[/]");
            }
        }
        else
        {
            lines.Add("[grey50 italic]No recent updates[/]");
        }
        lines.Add("");

        // Needs Attention section
        if (outdatedPackages.Any())
        {
            lines.Add("[yellow bold]Needs Attention:[/]");
            foreach (var pkg in outdatedPackages.Take(5))
            {
                lines.Add($"[yellow]⚠ {Markup.Escape(pkg.Id)}[/]");
                lines.Add($"  [grey70]{pkg.Version} → {pkg.LatestVersion} available[/]");
            }
        }
        else
        {
            lines.Add("[green bold]✓ All packages up-to-date![/]");
        }
        lines.Add("");

        // Quick Actions
        lines.Add("[cyan1 bold]Quick Actions:[/]");
        lines.Add("[grey70][Enter] View packages[/]");
        lines.Add("[grey70][Ctrl+U] Update all outdated[/]");
        lines.Add("[grey70][Ctrl+R] Restore packages[/]");

        return lines;
    }
}
```

**Usage in LazyNuGetWindow:**
```csharp
private void OnProjectSelected(ProjectInfo project)
{
    _selectedProject = project;

    // Build dashboard content
    var dashboardLines = ProjectDashboardBuilder.BuildDashboard(project, outdatedPackages);

    // Update right panel
    _detailsPanel?.ClearControls();
    var dashboardContent = Controls.Markup()
        .WithLines(dashboardLines)
        .WithMargin(1, 1, 1, 1)
        .Build();
    _detailsPanel?.AddControl(dashboardContent);
}
```

**Card Borders:** Use box drawing characters for visual separation:
- `┌─┬─┐` (top)
- `│ │ │` (sides)
- `└─┴─┘` (bottom)

**Reference:** ConsoleTop example shows similar dashboard cards with stats

## Critical Files to Reference

1. **Window Layout:** `/home/nick/source/ConsoleEx/Examples/AgentStudio/AgentStudioWindow.cs`
   - 3-panel HorizontalGrid setup (lines 114-161)
   - Sticky top/bottom bars (lines 75-101, 186-200)
   - Event wiring (lines 254-310)

2. **File Dialogs:** `/home/nick/source/ConsoleEx/SharpConsoleUI/Dialogs/FileDialogs.cs`
   - `ShowFolderPickerAsync()` implementation
   - Modal creation pattern

3. **Modal Pattern:** `/home/nick/source/ConsoleEx/Examples/AgentStudio/Modals/CommandPaletteModal.cs`
   - TaskCompletionSource pattern
   - Keyboard handling in modals

4. **Service Pattern:** `/home/nick/source/ConsoleEx/Examples/AgentStudio/Services/MockAiService.cs`
   - Async data loading
   - UI update patterns

5. **Driver Setup:** `/home/nick/source/ConsoleEx/Examples/AgentStudio/Program.cs`
   - NetConsoleDriver initialization
   - ConsoleWindowSystem configuration

## Implementation Phases

### Phase 1: Foundation (MVP Core) ✅ COMPLETED
1. ✅ Create LazyNuGet.csproj with conditional ConsoleEx reference
2. ✅ Implement Program.cs with CLI args (use current directory if no arg)
3. ✅ Build LazyNuGetWindow with 2-panel layout (empty)
4. ✅ Add ColorScheme utility class
5. ✅ Add top menu with File > Open Folder, Reload, Exit
6. ✅ Integrate FileDialogs.ShowFolderPickerAsync()
7. ✅ Implement context-switching logic (Projects → Packages → Search)

### Phase 2: Project Discovery & Dashboard
1. ✅ Create Models (ProjectInfo, PackageReference, NuGetPackage)
2. ✅ Implement ProjectDiscoveryService (find .csproj recursively)
3. ✅ Implement ProjectParserService (parse XML with System.Xml.Linq)
4. ✅ Wire up folder selection → project list display
5. **Build ProjectDashboardBuilder** (stats cards, recent updates, needs attention)
6. Wire project selection → dashboard display in right panel
7. Add breadcrumb updates in top status bar

### Phase 3: Package List & Details
1. Implement NuGetClientService (search and details API)
2. Wire Enter on project → switch to package list view
3. Build PackageDetailsBuilder for right panel
4. Wire package selection → details loading
5. Add version color indicators (green/yellow/red)
6. Add Esc key → go back to projects view

### Phase 4: Package Operations
1. Implement DotNetCliService (add/remove/restore)
2. Build SearchPackageModal with live search
3. Build ConfirmationModal and ErrorModal
4. Wire up Ctrl+S search flow → search results in left panel
5. Wire up Ctrl+I install flow with project selector
6. Add Ctrl+U update, Ctrl+X remove operations
7. Add error handling with user feedback

### Phase 5: Polish & Refinements
1. Implement async refresh thread (clock + outdated checks)
2. Add ConfigurationService (persist last folder, favorites)
3. Refine context-aware bottom help bar (changes with view)
4. Add "Update All" quick action from dashboard
5. Add loading indicators for async operations
6. Test all operations end-to-end
7. Cross-platform testing (Windows/Linux/macOS)

## Verification

**Manual Testing:**
1. Launch with argument: `dotnet run --project LazyNuGet /path/to/projects`
2. Launch without argument: `dotnet run --project LazyNuGet` (uses current directory)
3. Verify: Projects listed in left panel
3. Click project → verify packages in middle panel
4. Click package → verify details in right panel
5. Press Ctrl+S → search for package → verify results
6. Select package → verify version selection
7. Confirm install → verify `dotnet add package` executes
8. Check .csproj file → verify PackageReference added
9. Press Ctrl+R → remove package → verify removed
10. Test error cases: network failure, invalid package, locked file

**Expected Behavior:**
- Clean AgentStudio-style UI with grey scale colors
- Smooth keyboard navigation between panels
- Live updates (clock, outdated indicators)
- Graceful error handling with detailed modals
- Settings persist across sessions (last folder)

## Success Criteria

✅ Can open folder via CLI or file dialog
✅ Can view projects and packages in 3-panel layout
✅ Can search NuGet.org for packages
✅ Can install/remove packages using dotnet CLI
✅ Shows package details from NuGet.org API
✅ Handles errors gracefully with user feedback
✅ AgentStudio-quality UI polish
✅ Works on Windows, Linux, macOS
