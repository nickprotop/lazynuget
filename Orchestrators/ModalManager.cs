using SharpConsoleUI;
using LazyNuGet.Models;
using LazyNuGet.Services;
using LazyNuGet.UI.Modals;

namespace LazyNuGet.Orchestrators;

/// <summary>
/// Manages modal lifecycle and coordination.
/// Centralizes all modal-related operations (settings, dependencies, confirmation, etc.)
/// </summary>
public class ModalManager
{
    private readonly ConsoleWindowSystem _windowSystem;
    private readonly ConfigurationService? _configService;
    private readonly NuGetConfigService _nugetConfigService;
    private readonly DotNetCliService _cliService;
    private readonly Window? _parentWindow;

    private string _currentFolderPath;
    private NuGetClientService _nugetService;

    public ModalManager(
        ConsoleWindowSystem windowSystem,
        ConfigurationService? configService,
        NuGetConfigService nugetConfigService,
        DotNetCliService cliService,
        NuGetClientService nugetService,
        string currentFolderPath,
        Window? parentWindow)
    {
        _windowSystem = windowSystem;
        _configService = configService;
        _nugetConfigService = nugetConfigService;
        _cliService = cliService;
        _nugetService = nugetService;
        _currentFolderPath = currentFolderPath;
        _parentWindow = parentWindow;
    }

    /// <summary>
    /// Update the NuGet service instance (used after settings changes)
    /// </summary>
    public void SetNuGetService(NuGetClientService service)
    {
        _nugetService = service;
    }

    /// <summary>
    /// Update the current folder path
    /// </summary>
    public void SetFolderPath(string folderPath)
    {
        _currentFolderPath = folderPath;
    }

    /// <summary>
    /// Show the settings modal and return whether settings changed
    /// </summary>
    public async Task<bool> ShowSettingsAsync()
    {
        if (_configService == null || _nugetService == null) return false;

        var changed = await SettingsModal.ShowAsync(
            _windowSystem, _configService, _nugetConfigService, _cliService, _nugetService, _currentFolderPath, _parentWindow);

        return changed;
    }

    /// <summary>
    /// Show the dependency tree modal for a project
    /// </summary>
    public async Task ShowDependencyTreeAsync(ProjectInfo project, PackageReference? selectedPackage = null)
    {
        await DependencyTreeModal.ShowAsync(_windowSystem, _cliService, _nugetService, project, selectedPackage, _parentWindow);
    }

    /// <summary>
    /// Show exit confirmation dialog
    /// </summary>
    public async Task<bool> ConfirmExitAsync()
    {
        var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
            "Exit LazyNuGet",
            "Are you sure you want to exit?",
            yesText: "Exit", noText: "Cancel",
            parentWindow: _parentWindow);
        return confirm;
    }
}
