using System.Reflection;
using SharpConsoleUI;
using SharpConsoleUI.Configuration;
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
        string folderPath = args.Length > 0
            ? args[0]
            : Environment.CurrentDirectory;

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
            var windowSystem = new ConsoleWindowSystem(
                driver,
                options: new ConsoleWindowSystemOptions(
                    StatusBarOptions: new StatusBarOptions(
                        ShowTaskBar: false,
                        ShowBottomStatus: false
                    )
                ));

            // Set default log level to Information
            windowSystem.LogService.MinimumLevel = SharpConsoleUI.Logging.LogLevel.Information;

            using var mainWindow = new LazyNuGetWindow(windowSystem, folderPath, configService);
            mainWindow.Show();
            await Task.Run(() => windowSystem.Run());

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
