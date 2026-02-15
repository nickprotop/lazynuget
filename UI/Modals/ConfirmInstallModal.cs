using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Package installation confirmation dialog with vulnerability warnings and SDK compatibility checks
/// </summary>
public class ConfirmInstallModal : ModalBase<bool>
{
    private readonly NuGetPackage _package;
    private readonly string _projectName;
    private readonly string _message;
    private readonly string? _projectTargetFramework;
    private readonly bool _hasCompatibilityWarning;
    private readonly string? _compatibilityWarningMessage;

    private ConfirmInstallModal(NuGetPackage package, string projectName, string message, string? projectTargetFramework = null)
    {
        _package = package;
        _projectName = projectName;
        _message = message;
        _projectTargetFramework = projectTargetFramework;

        // Check for compatibility issues
        (_hasCompatibilityWarning, _compatibilityWarningMessage) = CheckCompatibility(package, projectTargetFramework);
    }

    /// <summary>
    /// Show an installation confirmation dialog with vulnerability warnings and SDK compatibility checks
    /// </summary>
    public static Task<bool> ShowAsync(
        ConsoleWindowSystem windowSystem,
        NuGetPackage package,
        string projectName,
        string message,
        string? projectTargetFramework = null,
        Window? parentWindow = null)
    {
        var modal = new ConfirmInstallModal(package, projectName, message, projectTargetFramework);
        return modal.ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => "Install Package";

    protected override (int width, int height) GetSize()
    {
        // Increase height if vulnerabilities are present
        var baseHeight = 14;
        var extraHeight = _package.VulnerabilityCount > 0 ? 4 : 0;

        // Increase height if compatibility warning is present
        if (_hasCompatibilityWarning)
        {
            extraHeight += 4; // Warning header + message + spacing
        }

        // Increase height if dependencies are present
        var dependencyCount = GetTotalDependencyCount();
        if (dependencyCount > 0)
        {
            extraHeight += 3; // Header + first dep + spacing
            extraHeight += Math.Min(dependencyCount - 1, 4); // Additional deps (max 5 shown)
            if (dependencyCount > 5)
            {
                extraHeight += 1; // "... and X more" line
            }
        }

        return (60, baseHeight + extraHeight);
    }

    private int GetTotalDependencyCount()
    {
        return _package.Dependencies
            .SelectMany(g => g.Packages)
            .Count();
    }

    protected override BorderStyle GetBorderStyle() => BorderStyle.Single;

    protected override Color GetBorderColor()
    {
        // Use red border if vulnerabilities are present
        if (_package.VulnerabilityCount > 0)
            return Color.Red;

        // Use orange border if compatibility warning is present
        if (_hasCompatibilityWarning)
            return Color.Orange1;

        return Color.Grey35;
    }

    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        // Title header
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup}]Install Package[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(1, 1, 0, 0)
            .Build());

        // Vulnerability warning (if applicable)
        if (_package.VulnerabilityCount > 0)
        {
            var warningColor = _package.VulnerabilityCount >= 3 ? "red" : "orange1";
            var warningIcon = _package.VulnerabilityCount >= 3 ? "🔴" : "⚠️";

            Modal.AddControl(Controls.Markup()
                .AddLine($"[{warningColor} bold]{warningIcon} WARNING: This package has {_package.VulnerabilityCount} known {(_package.VulnerabilityCount == 1 ? "vulnerability" : "vulnerabilities")}![/]")
                .AddLine($"[yellow]Review security advisories before installing.[/]")
                .WithAlignment(HorizontalAlignment.Center)
                .WithMargin(1, 1, 1, 0)
                .Build());
        }

        // Compatibility warning (if applicable)
        if (_hasCompatibilityWarning && !string.IsNullOrEmpty(_compatibilityWarningMessage))
        {
            Modal.AddControl(Controls.Markup()
                .AddLine($"[orange1 bold]⚠ COMPATIBILITY WARNING[/]")
                .AddLine($"[yellow]{Markup.Escape(_compatibilityWarningMessage)}[/]")
                .WithAlignment(HorizontalAlignment.Center)
                .WithMargin(1, 1, 1, 0)
                .Build());
        }

        // Message body
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(_message)}[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(1, 1, 1, 1)
            .Build());

        // Package info
        var packageInfo = $"[{ColorScheme.MutedMarkup}]Package: {Markup.Escape(_package.Id)} v{Markup.Escape(_package.Version)}[/]";
        if (_package.IsDeprecated)
        {
            packageInfo = $"[orange1]⚠ DEPRECATED[/] {packageInfo}";
        }

        Modal.AddControl(Controls.Markup()
            .AddLine(packageInfo)
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(1, 0, 0, 0)
            .Build());

        // Dependency tree
        AddDependencyTree();

        // Buttons
        var installButton = Controls.Button("  Install (Y)  ")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) => CloseWithResult(true))
            .Build();

        var cancelButton = Controls.Button("  Cancel (N)  ")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) => CloseWithResult(false))
            .Build();

        // Bottom rule before buttons
        Modal.AddControl(Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(ColorScheme.RuleColor)
            .Build());

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(installButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(cancelButton))
            .Build();
        buttonGrid.Margin = new Margin(0, 1, 0, 0);
        Modal.AddControl(buttonGrid);

        // Bottom hint
        Modal.AddControl(Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(ColorScheme.RuleColor)
            .Build());

        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Y:Install  N/Esc:Cancel[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Build());
    }

    private void AddDependencyTree()
    {
        var allDependencies = _package.Dependencies
            .SelectMany(g => g.Packages)
            .ToList();

        if (allDependencies.Count == 0)
            return;

        var markupBuilder = Controls.Markup();

        // Header
        markupBuilder.AddLine($"[{ColorScheme.MutedMarkup}]Dependencies ({allDependencies.Count}):[/]");

        // Show first 5 dependencies
        var displayCount = Math.Min(5, allDependencies.Count);
        for (int i = 0; i < displayCount; i++)
        {
            var dep = allDependencies[i];
            var isLast = i == displayCount - 1 && allDependencies.Count <= 5;
            var branch = isLast ? "└─" : "├─";

            var versionText = string.IsNullOrEmpty(dep.VersionRange)
                ? ""
                : $" [{ColorScheme.MutedMarkup}]({FormatVersionRange(dep.VersionRange)})[/]";

            markupBuilder.AddLine($"[{ColorScheme.MutedMarkup}]{branch}[/] {Markup.Escape(dep.Id)}{versionText}");
        }

        // Show "... and X more" if there are more than 5 dependencies
        if (allDependencies.Count > 5)
        {
            var remaining = allDependencies.Count - 5;
            markupBuilder.AddLine($"[{ColorScheme.MutedMarkup}]└─ ... and {remaining} more[/]");
        }

        Modal.AddControl(markupBuilder
            .WithAlignment(HorizontalAlignment.Left)
            .WithMargin(2, 1, 1, 0)
            .Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Y || e.KeyInfo.Key == ConsoleKey.Enter)
        {
            CloseWithResult(true);
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.N)
        {
            CloseWithResult(false);
            e.Handled = true;
        }
        else
        {
            // Let base handle Escape
            base.OnKeyPressed(sender, e);
        }
    }

    /// <summary>
    /// Check if the package target frameworks are compatible with the project target framework
    /// </summary>
    private static (bool hasWarning, string? warningMessage) CheckCompatibility(NuGetPackage package, string? projectTargetFramework)
    {
        // Skip if no project framework or no package frameworks
        if (string.IsNullOrEmpty(projectTargetFramework) ||
            package.TargetFrameworks == null ||
            package.TargetFrameworks.Count == 0)
        {
            return (false, null);
        }

        // Parse project framework version
        var projectVersion = ParseFrameworkVersion(projectTargetFramework);
        if (projectVersion == null)
        {
            return (false, null);
        }

        // Get the highest package framework version
        var highestPackageVersion = package.TargetFrameworks
            .Select(ParseFrameworkVersion)
            .Where(v => v != null)
            .OrderByDescending(v => v)
            .FirstOrDefault();

        if (highestPackageVersion == null)
        {
            return (false, null);
        }

        // Check if package requires a newer framework than the project
        if (highestPackageVersion > projectVersion)
        {
            // Find the specific framework string that caused the issue
            var problematicFramework = package.TargetFrameworks
                .FirstOrDefault(tf => ParseFrameworkVersion(tf) == highestPackageVersion);

            var message = $"Package targets {problematicFramework ?? highestPackageVersion.ToString()} but project uses {projectTargetFramework}. Installation may fail or cause compatibility issues.";
            return (true, message);
        }

        return (false, null);
    }

    /// <summary>
    /// Format NuGet version range for display (make it more readable)
    /// </summary>
    private static string FormatVersionRange(string range)
    {
        // Common patterns in NuGet version ranges:
        // [1.0.0, )     = >= 1.0.0
        // [1.0.0, 2.0.0) = >= 1.0.0 && < 2.0.0
        // (,2.0.0)      = < 2.0.0

        if (string.IsNullOrEmpty(range))
            return "any";

        // Clean up "any version" patterns
        if (range == "[, )" || range == "(, )")
            return "any";

        // Clean up ">= X" patterns
        if (range.StartsWith("[") && range.EndsWith(", )"))
        {
            var version = range.Substring(1, range.Length - 4).Trim();
            return $">= {version}";
        }

        // Just escape and return as-is for complex ranges
        return Markup.Escape(range);
    }

    /// <summary>
    /// Parse .NET framework version from framework moniker (e.g., "net8.0" -> 8.0, "netstandard2.1" -> 2.1)
    /// </summary>
    private static double? ParseFrameworkVersion(string framework)
    {
        if (string.IsNullOrEmpty(framework))
            return null;

        framework = framework.ToLowerInvariant().Trim();

        if (framework.Length < 4)
            return null;

        // Handle .NET (Core) format: net6.0, net7.0, net8.0, etc.
        if (framework.StartsWith("net") && !framework.StartsWith("netstandard") && !framework.StartsWith("netframework"))
        {
            var versionPart = framework.Substring(3);
            if (double.TryParse(versionPart, out var version))
            {
                return version;
            }
        }

        // Handle .NET Standard format: netstandard2.0, netstandard2.1
        if (framework.StartsWith("netstandard"))
        {
            var versionPart = framework.Substring(11);
            if (double.TryParse(versionPart, out var version))
            {
                // Map netstandard versions to approximate .NET versions for comparison
                // netstandard2.0 ~= net5.0, netstandard2.1 ~= net5.0
                return version + 3.0; // Simple heuristic
            }
        }

        // Handle .NET Framework format: net472, net48, etc.
        if (framework.StartsWith("net") && char.IsDigit(framework[3]))
        {
            var versionPart = framework.Substring(3);
            if (int.TryParse(versionPart, out var intVersion))
            {
                // Convert to comparable format: 472 -> 4.72
                if (intVersion >= 100)
                {
                    return intVersion / 100.0;
                }
            }
        }

        return null;
    }
}
