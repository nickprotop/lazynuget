using SharpConsoleUI.Controls;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;

namespace LazyNuGet.Orchestrators;

/// <summary>
/// Manages package filter mode, filter input, and filtered list display.
/// Extracted from LazyNuGetWindow to reduce god-class complexity.
/// </summary>
public class PackageFilterController
{
    private readonly ListControl? _contextList;
    private readonly MarkupControl? _filterDisplay;
    private readonly MarkupControl? _leftPanelHeader;
    private readonly MarkupControl? _bottomHelpBar;
    private readonly StatusBarManager? _statusBarManager;
    private readonly Func<ViewState> _getCurrentViewState;
    private readonly Action<IEnumerable<PackageReference>> _populatePackagesList;
    private readonly Action<List<string>> _updateDetailsContent;
    private readonly Action _handleSelectionChanged;

    private bool _filterMode;
    private string _packageFilter = string.Empty;
    private List<PackageReference> _allInstalledPackages = new();

    public bool IsFilterMode => _filterMode;
    public string FilterText => _packageFilter;
    public List<PackageReference> AllInstalledPackages => _allInstalledPackages;

    public PackageFilterController(
        ListControl? contextList,
        MarkupControl? filterDisplay,
        MarkupControl? leftPanelHeader,
        MarkupControl? bottomHelpBar,
        StatusBarManager? statusBarManager,
        Func<ViewState> getCurrentViewState,
        Action<IEnumerable<PackageReference>> populatePackagesList,
        Action<List<string>> updateDetailsContent,
        Action handleSelectionChanged)
    {
        _contextList = contextList;
        _filterDisplay = filterDisplay;
        _leftPanelHeader = leftPanelHeader;
        _bottomHelpBar = bottomHelpBar;
        _statusBarManager = statusBarManager;
        _getCurrentViewState = getCurrentViewState;
        _populatePackagesList = populatePackagesList;
        _updateDetailsContent = updateDetailsContent;
        _handleSelectionChanged = handleSelectionChanged;
    }

    public void SetPackages(List<PackageReference> packages)
    {
        _allInstalledPackages = packages;
    }

    public void ResetFilter()
    {
        _filterMode = false;
        _packageFilter = string.Empty;
        if (_filterDisplay != null)
            _filterDisplay.Visible = false;
    }

    public void EnterFilterMode()
    {
        if (_getCurrentViewState() != ViewState.Packages) return;

        _filterMode = true;
        _packageFilter = string.Empty;

        if (_filterDisplay != null)
        {
            _filterDisplay.Visible = true;
            UpdateFilterDisplay();
        }

        if (_bottomHelpBar != null)
        {
            _bottomHelpBar.SetContent(new List<string> {
                $"[{ColorScheme.SecondaryMarkup}]Filter Mode:[/] Type to filter | " +
                $"[{ColorScheme.MutedMarkup}]Backspace to delete | Esc to exit[/]"
            });
        }
    }

    public void ExitFilterMode()
    {
        _filterMode = false;
        _packageFilter = string.Empty;

        if (_filterDisplay != null)
        {
            _filterDisplay.Visible = false;
        }

        // Reset to show all packages
        _populatePackagesList(_allInstalledPackages);

        if (_leftPanelHeader != null)
        {
            _leftPanelHeader.SetContent(new List<string> { $"[grey70]Packages ({_allInstalledPackages.Count})[/]" });
        }

        _statusBarManager?.UpdateHelpBar(_getCurrentViewState());
    }

    public void HandleFilterInput(ConsoleKeyInfo keyInfo)
    {
        if (keyInfo.Key == ConsoleKey.Backspace)
        {
            if (_packageFilter.Length > 0)
            {
                _packageFilter = _packageFilter.Substring(0, _packageFilter.Length - 1);
                UpdateFilterDisplay();
                FilterInstalledPackages();
            }
        }
        else if (!char.IsControl(keyInfo.KeyChar))
        {
            _packageFilter += keyInfo.KeyChar;
            UpdateFilterDisplay();
            FilterInstalledPackages();
        }
    }

    public IEnumerable<PackageReference> GetFilteredPackages()
    {
        if (_filterMode && !string.IsNullOrWhiteSpace(_packageFilter))
            return _allInstalledPackages.Where(p => p.Id.Contains(_packageFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        return _allInstalledPackages;
    }

    private void UpdateFilterDisplay()
    {
        if (_filterDisplay == null) return;

        var filterText = string.IsNullOrEmpty(_packageFilter) ? "_" : Markup.Escape(_packageFilter);
        _filterDisplay.SetContent(new List<string> {
            $"[{ColorScheme.MutedMarkup}]Filter: [/][{ColorScheme.PrimaryMarkup}]{filterText}[/] " +
            $"[{ColorScheme.MutedMarkup}](Esc to clear)[/]"
        });
    }

    private void FilterInstalledPackages()
    {
        var query = _packageFilter.ToLowerInvariant();

        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allInstalledPackages
            : _allInstalledPackages.Where(p => p.Id.ToLowerInvariant().Contains(query)).ToList();

        _populatePackagesList(filtered);

        if (_leftPanelHeader != null)
        {
            var headerText = string.IsNullOrWhiteSpace(query)
                ? $"[grey70]Packages ({_allInstalledPackages.Count})[/]"
                : $"[grey70]Packages ({filtered.Count} of {_allInstalledPackages.Count})[/]";
            _leftPanelHeader.SetContent(new List<string> { headerText });
        }

        if (_contextList != null && _contextList.Items.Count > 0)
        {
            var wasZero = _contextList.SelectedIndex == 0;
            _contextList.SelectedIndex = 0;
            if (wasZero)
            {
                _handleSelectionChanged();
            }
        }
        else
        {
            _updateDetailsContent(new List<string> { "[grey50]No matching packages found[/]" });
        }
    }
}
