using System.Reflection;
using SharpConsoleUI;
using LazyNuGet.UI.Utilities;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Drivers;
using LazyNuGet.Services;

namespace LazyNuGet;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Handle --version / -v
        if (args.Any(a => a == "--version" || a == "-v"))
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown";
            Console.WriteLine($"lazynuget {version}");
            return 0;
        }

        // Handle --help / -h
        if (args.Any(a => a == "--help" || a == "-h"))
        {
            Console.WriteLine("Usage: lazynuget [path]");
            Console.WriteLine("       lazynuget --migrate [path]");
            Console.WriteLine("       lazynuget --migrate-cpm [path]");
            Console.WriteLine();
            Console.WriteLine("  path           Folder to open (default: current directory)");
            Console.WriteLine("  --migrate      Migrate packages.config projects to PackageReference");
            Console.WriteLine("  --migrate-cpm  Migrate PackageReference projects to Central Package Management");
            Console.WriteLine("  --version      Show version information");
            Console.WriteLine("  --help         Show this help message");
            return 0;
        }

        // Handle --migrate (headless, no UI)
        if (args.Length > 0 && args[0] == "--migrate")
        {
            string targetPath = args.Length > 1
                ? Path.GetFullPath(args[1])
                : Environment.CurrentDirectory;

            if (!Directory.Exists(targetPath))
            {
                Console.Error.WriteLine($"Error: Directory '{targetPath}' does not exist.");
                return 1;
            }

            var migrationService = new PackagesConfigMigrationService();
            var results = await migrationService.MigrateAllInFolderAsync(
                targetPath,
                progress: new Progress<string>(Console.WriteLine));

            foreach (var r in results)
            {
                if (r.Success)
                    Console.WriteLine($"  ✓ {Path.GetFileName(r.ProjectPath)} — {r.PackagesMigrated} package(s) migrated");
                else
                    Console.WriteLine($"  ✗ {Path.GetFileName(r.ProjectPath)} — {r.Error}");
            }

            if (!results.Any())
                Console.WriteLine("No packages.config projects found.");

            return results.All(r => r.Success) ? 0 : 1;
        }

        // Handle --migrate-cpm (headless, no UI)
        if (args.Length > 0 && args[0] == "--migrate-cpm")
        {
            string targetPath = args.Length > 1
                ? Path.GetFullPath(args[1])
                : Environment.CurrentDirectory;

            if (!Directory.Exists(targetPath))
            {
                Console.Error.WriteLine($"Error: Directory '{targetPath}' does not exist.");
                return 1;
            }

            var svc      = new CpmMigrationService();
            var analysis = await svc.AnalyzeAsync(
                targetPath, new Progress<string>(Console.WriteLine));

            if (analysis.ProjectsToMigrate.Count == 0)
            {
                Console.WriteLine("No projects to migrate (all already use CPM or packages.config).");
                return 0;
            }

            var result = await svc.MigrateAsync(
                targetPath, analysis, new Progress<string>(Console.WriteLine));

            if (result.Success)
                Console.WriteLine(
                    $"  ✓ {result.ProjectsMigrated} project(s) migrated, " +
                    $"{result.PackagesCentralized} package(s) centralized" +
                    (result.VersionConflictsResolved > 0
                        ? $", {result.VersionConflictsResolved} conflict(s) resolved (highest version used)"
                        : string.Empty));
            else
                Console.WriteLine($"  ✗ {result.Error}");

            return result.Success ? 0 : 1;
        }

        var configService = new ConfigurationService();
        var settings = configService.Load();

        // Priority: CLI arg > current directory
        // Note: LastFolderPath is tracked for UI history, not used as default
        // The first NON-FLAG argument is the folder: args[0] alone would read "--debug-socket"
        // as a directory name and fail validation before the UI ever starts.
        string? folderArg = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith('-'))
            {
                // --debug-socket takes an optional value; skip it so it is not read as a path.
                if (args[i] == "--debug-socket" && i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    i++;
                continue;
            }
            folderArg = args[i];
            break;
        }

        string folderPath = folderArg ?? Environment.CurrentDirectory;

        // Resolve to absolute path to avoid issues with relative paths
        folderPath = Path.GetFullPath(folderPath);

        // Validate folder exists
        if (!Directory.Exists(folderPath))
        {
            Console.Error.WriteLine($"Error: Directory '{folderPath}' does not exist.");
            return 1;
        }

        try
        {
            var driverOptions = new NetConsoleDriverOptions
            {
                RenderMode = RenderMode.Buffer
            };
            var driver = new NetConsoleDriver(driverOptions);
            // --debug-socket opens the diagnostics socket cxdbg attaches to. Set in code on
            // purpose: the SHARPCONSOLEUI_DIAGNOSTICS_SOCKET route prompts for consent, and
            // the prompt has to happen before the driver paints, which is not reachable from
            // here. A flag cannot be inherited from a shell or a unit file, so it can only be
            // present because someone typed it.
            string? debugSocket = null;
            int socketArg = Array.FindIndex(args, a => a == "--debug-socket");
            if (socketArg >= 0)
            {
                debugSocket = socketArg + 1 < args.Length && !args[socketArg + 1].StartsWith('-')
                    ? args[socketArg + 1]
                    : "/tmp/lazynuget.sock";
            }

            var windowSystem = new ConsoleWindowSystem(
                driver,
                options: new ConsoleWindowSystemOptions(
                    ShowTopPanel: false,
                    ShowBottomPanel: false,
                    InstallSynchronizationContext: true,
                    DiagnosticsSocket: debugSocket));

            // The stock theme pins scrollbar thumbs to Cyan1, which against these dark greys is
            // the loudest thing on screen: a full-height accent bar down the sidebar, brighter
            // than any actual content. A scrollbar reports position; it should not lead the
            // page. Focused stays a step above unfocused so it still shows which pane scrolls.
            // ITheme exposes these read-only; ThemeBase is where they are settable.
            if (windowSystem.Theme is SharpConsoleUI.Themes.ThemeBase theme)
            {
            theme.ScrollbarThumbColor = ColorScheme.ScrollbarThumbColor;
            theme.ScrollbarThumbUnfocusedColor = ColorScheme.ScrollbarThumbUnfocusedColor;
            theme.ScrollbarTrackColor = ColorScheme.ScrollbarTrackColor;
            theme.ScrollbarTrackUnfocusedColor = ColorScheme.ScrollbarTrackColor;
            theme.TableScrollbarThumbColor = ColorScheme.ScrollbarThumbColor;
            theme.TableScrollbarTrackColor = ColorScheme.ScrollbarTrackColor;
            }

            // Set default log level to Information
            windowSystem.LogService.MinimumLevel = SharpConsoleUI.Logging.LogLevel.Information;

            using var mainWindow = new LazyNuGetWindow(windowSystem, folderPath, configService);
            mainWindow.Show();
            await Task.Run(() => windowSystem.Run());

            return 0;
        }
        catch (Exception ex)
        {
            Console.Clear();
            ExceptionFormatter.WriteException(ex);
            return 1;
        }
    }
}
