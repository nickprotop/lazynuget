using SharpConsoleUI;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Drivers;

namespace LazyNuGet;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Use CLI arg if provided, otherwise use current directory
        string folderPath = args.Length > 0 ? args[0] : Environment.CurrentDirectory;

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

            using var mainWindow = new LazyNuGetWindow(windowSystem, folderPath);
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
