using SharpConsoleUI;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Drivers;
using LazyNuGet.Services;

namespace LazyNuGet;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var configService = new ConfigurationService();
        var settings = configService.Load();

        // Priority: CLI arg > current directory
        // Note: LastFolderPath is tracked for UI history, not used as default
        string folderPath = args.Length > 0
            ? args[0]
            : Environment.CurrentDirectory;

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
