using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;

namespace LazyNuGet.UI.Components;

/// <summary>
/// Builder for interactive package details with tabbed interface (F1-F5)
/// </summary>
public static class InteractivePackageDetailsBuilder
{
    public static List<IWindowControl> BuildInteractiveDetails(
        PackageReference package,
        NuGetPackage? nugetData,
        PackageDetailTab activeTab,
        Action onUpdate,
        Action onChangeVersion,
        Action onRemove,
        Action? onDeps = null)
    {
        var controls = new List<IWindowControl>();

        // Package header
        var header = Controls.Markup()
            .AddLine($"[cyan1 bold]Package: {Markup.Escape(package.Id)}[/]")
            .AddLine($"[grey70]Installed: {Markup.Escape(package.Version)}[/]")
            .AddEmptyLine()
            .WithMargin(1, 1, 0, 0)
            .Build();
        controls.Add(header);

        // Status section
        var status = BuildStatusSection(package);
        controls.Add(status);

        // Separator before toolbar
        var separator1 = Controls.Rule("[grey70]Package Actions[/]");
        separator1.Margin = new Margin(1, 0, 1, 0);
        controls.Add(separator1);

        // Empty markup above toolbar for background extension
        var toolbarTop = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(1, 0, 1, 0)
            .Build();
        controls.Add(toolbarTop);

        // Action toolbar - placed after status, before details
        var toolbar = BuildActionToolbar(package, nugetData, onUpdate, onChangeVersion, onRemove, onDeps);
        controls.Add(toolbar);

        // Empty markup below toolbar for background extension
        var toolbarBottom = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(1, 0, 1, 0)
            .Build();
        controls.Add(toolbarBottom);

        // Separator after toolbar
        var separator2 = Controls.Rule();
        separator2.Margin = new Margin(1, 0, 1, 0);
        controls.Add(separator2);

        // Tab bar
        var tabBar = BuildTabBar(activeTab);
        controls.Add(tabBar);

        // Separator after tab bar
        var separator3 = Controls.Rule();
        separator3.Margin = new Margin(1, 0, 1, 0);
        controls.Add(separator3);

        // Tab content
        if (nugetData != null)
        {
            var tabContent = BuildTabContent(activeTab, nugetData, package);
            controls.Add(tabContent);
        }
        else
        {
            var loading = Controls.Markup()
                .AddLine("[grey50 italic]Loading details from NuGet.org...[/]")
                .WithMargin(1, 0, 0, 0)
                .Build();
            controls.Add(loading);
        }

        return controls;
    }

    private static MarkupControl BuildStatusSection(PackageReference package)
    {
        var builder = Controls.Markup();

        if (package.HasVulnerability)
        {
            builder.AddLine($"[red bold]⚠ SECURITY VULNERABILITY[/]");
            builder.AddEmptyLine();
        }

        if (package.IsOutdated && !string.IsNullOrEmpty(package.LatestVersion))
        {
            builder.AddLine($"[yellow bold]Update Available[/]");
            builder.AddLine($"[grey70]Latest Version: {Markup.Escape(package.LatestVersion)}[/]");
            builder.AddEmptyLine();
        }
        else if (!package.IsOutdated)
        {
            builder.AddLine($"[green bold]✓ Up to date[/]");
            builder.AddEmptyLine();
        }

        return builder.WithMargin(1, 0, 0, 0).Build();
    }

    private static MarkupControl BuildTabBar(PackageDetailTab activeTab)
    {
        var tabs = new (PackageDetailTab tab, string key, string label)[]
        {
            (PackageDetailTab.Overview, "F1", "Overview"),
            (PackageDetailTab.Dependencies, "F2", "Deps"),
            (PackageDetailTab.Versions, "F3", "Versions"),
            (PackageDetailTab.WhatsNew, "F4", "What's New"),
        };

        var parts = new List<string>();
        foreach (var (tab, key, label) in tabs)
        {
            if (tab == activeTab)
                parts.Add($"[white on grey35] {key} {label} [/]");
            else
                parts.Add($"[grey50]{key} {label}[/]");
        }

        return Controls.Markup()
            .AddLine(string.Join("  ", parts))
            .WithMargin(1, 1, 1, 0)
            .WithName("tabBar")
            .Build();
    }

    private static MarkupControl BuildTabContent(PackageDetailTab activeTab, NuGetPackage nugetData, PackageReference package)
    {
        return activeTab switch
        {
            PackageDetailTab.Overview => BuildOverviewPanel(nugetData, package),
            PackageDetailTab.Dependencies => BuildDependenciesPanel(nugetData),
            PackageDetailTab.Versions => BuildVersionsPanel(nugetData, package),
            PackageDetailTab.WhatsNew => BuildWhatsNewPanel(nugetData, package),
            _ => BuildOverviewPanel(nugetData, package),
        };
    }

    private static MarkupControl BuildOverviewPanel(NuGetPackage nugetData, PackageReference package)
    {
        var builder = Controls.Markup();

        // Verification and warning badges
        var badges = new List<string>();
        if (nugetData.IsVerified)
            badges.Add("[green]✓ Verified[/]");
        if (nugetData.VulnerabilityCount > 0)
            badges.Add($"[red]⚠ {nugetData.VulnerabilityCount} Vulnerabilities[/]");
        if (badges.Any())
        {
            builder.AddLine(string.Join(" ", badges));
            builder.AddEmptyLine();
        }

        // Deprecation warning
        if (nugetData.IsDeprecated)
        {
            builder.AddLine($"[red bold]⚠ DEPRECATED[/]");
            if (!string.IsNullOrEmpty(nugetData.DeprecationMessage))
                builder.AddLine($"[yellow]{Markup.Escape(nugetData.DeprecationMessage)}[/]");
            if (!string.IsNullOrEmpty(nugetData.AlternatePackageId))
                builder.AddLine($"[grey70]Alternative: {Markup.Escape(nugetData.AlternatePackageId)}[/]");
            builder.AddEmptyLine();
        }

        // Description
        if (!string.IsNullOrEmpty(nugetData.Description))
        {
            builder.AddLine($"[grey70 bold]Description:[/]");
            builder.AddLine($"[grey70]{Markup.Escape(nugetData.Description)}[/]");
            builder.AddEmptyLine();
        }

        // Authors (merged from Details tab)
        if (nugetData.Authors.Any())
        {
            var authors = string.Join(", ", nugetData.Authors);
            builder.AddLine($"[grey70]Authors: {Markup.Escape(authors)}[/]");
        }

        // License
        if (!string.IsNullOrEmpty(nugetData.LicenseExpression))
            builder.AddLine($"[grey70]License: {Markup.Escape(nugetData.LicenseExpression)}[/]");
        else if (!string.IsNullOrEmpty(nugetData.LicenseUrl))
            builder.AddLine($"[grey70]License: {Markup.Escape(nugetData.LicenseUrl)}[/]");

        // Downloads
        if (nugetData.TotalDownloads > 0)
            builder.AddLine($"[grey70]Downloads: {FormatDownloads(nugetData.TotalDownloads)}[/]");

        // Published
        if (nugetData.Published.HasValue)
            builder.AddLine($"[grey70]Published: {nugetData.Published.Value:yyyy-MM-dd}[/]");

        // Package Size (merged from Details tab)
        if (nugetData.PackageSize.HasValue)
            builder.AddLine($"[grey70]Package Size: {FormatSize(nugetData.PackageSize.Value)}[/]");

        // Tags (merged from Details tab)
        if (nugetData.Tags.Any())
        {
            var tags = string.Join(", ", nugetData.Tags);
            builder.AddLine($"[grey70]Tags: {Markup.Escape(tags)}[/]");
        }

        // Target Frameworks (merged from Details tab)
        if (nugetData.TargetFrameworks.Any())
        {
            var frameworks = string.Join(", ", nugetData.TargetFrameworks);
            builder.AddLine($"[grey70]Target Frameworks: {Markup.Escape(frameworks)}[/]");
        }

        // URLs (merged from Details tab)
        if (!string.IsNullOrEmpty(nugetData.ProjectUrl))
            builder.AddLine($"[grey70]Project URL: {Markup.Escape(nugetData.ProjectUrl)}[/]");
        if (!string.IsNullOrEmpty(nugetData.RepositoryUrl))
            builder.AddLine($"[grey70]Repository: {Markup.Escape(nugetData.RepositoryUrl)}[/]");

        // Version summary
        if (package.IsOutdated && !string.IsNullOrEmpty(package.LatestVersion))
        {
            builder.AddEmptyLine();
            builder.AddLine($"[yellow]Installed:[/] [grey70]{Markup.Escape(package.Version)}[/]  [yellow]Latest:[/] [grey70]{Markup.Escape(package.LatestVersion)}[/]");
        }

        builder.AddEmptyLine();
        return builder.WithMargin(1, 0, 0, 0).Build();
    }

    private static MarkupControl BuildDependenciesPanel(NuGetPackage nugetData)
    {
        // Tree guide characters (same as DependencyTreeModal)
        const string Branch = "├── ";
        const string Last   = "└── ";
        const string Pipe   = "│   ";
        const string Blank  = "    ";

        var builder = Controls.Markup();

        if (!nugetData.Dependencies.Any())
        {
            builder.AddLine("[grey50]No dependencies[/]");
            builder.AddEmptyLine();
            return builder.WithMargin(1, 0, 0, 0).Build();
        }

        var totalDeps = nugetData.Dependencies.Sum(g => g.Packages.Count);
        builder.AddLine($"[grey70 bold]Dependencies ({totalDeps} total):[/]");
        builder.AddEmptyLine();

        // Root: package name
        builder.AddLine($"[cyan1 bold]{Markup.Escape(nugetData.Id)}[/] [grey70]{Markup.Escape(nugetData.Version)}[/]");

        // Render tree structure
        for (var gi = 0; gi < nugetData.Dependencies.Count; gi++)
        {
            var group = nugetData.Dependencies[gi];
            var isLastGroup = gi == nugetData.Dependencies.Count - 1;
            var groupGuide = isLastGroup ? Last : Branch;
            var groupPipe = isLastGroup ? Blank : Pipe;

            var fwLabel = string.IsNullOrEmpty(group.TargetFramework) ? "(any)" : group.TargetFramework;
            builder.AddLine($"[grey50]{groupGuide}[/][green]{Markup.Escape(fwLabel)}[/] [grey50]({group.Packages.Count})[/]");

            if (group.Packages.Count == 0)
            {
                builder.AddLine($"[grey50]{groupPipe}{Last}[/][grey50](no dependencies)[/]");
            }
            else
            {
                for (var di = 0; di < group.Packages.Count; di++)
                {
                    var dep = group.Packages[di];
                    var isLastDep = di == group.Packages.Count - 1;
                    var depGuide = isLastDep ? Last : Branch;
                    var versionInfo = string.IsNullOrEmpty(dep.VersionRange) ? "" : $" [grey50]({dep.VersionRange})[/]";
                    builder.AddLine($"[grey50]{groupPipe}{depGuide}[/][grey70]{Markup.Escape(dep.Id)}[/]{versionInfo}");
                }
            }
        }

        builder.AddEmptyLine();
        return builder.WithMargin(1, 0, 0, 0).Build();
    }

    private static MarkupControl BuildVersionsPanel(NuGetPackage nugetData, PackageReference package)
    {
        var builder = Controls.Markup();

        if (!nugetData.Versions.Any())
        {
            builder.AddLine("[grey50]No version history available[/]");
            builder.AddEmptyLine();
            return builder.WithMargin(1, 0, 0, 0).Build();
        }

        const int MaxRecentVersions = 20;
        var totalCount = nugetData.Versions.Count;
        var recentVersions = nugetData.Versions.Take(MaxRecentVersions).ToList();
        var installedVersion = package.Version;
        var installedInRecent = recentVersions.Contains(installedVersion);

        // Header
        if (totalCount > MaxRecentVersions)
            builder.AddLine($"[grey70 bold]Available Versions ({totalCount} total, showing {MaxRecentVersions} most recent):[/]");
        else
            builder.AddLine($"[grey70 bold]Available Versions ({totalCount}):[/]");

        builder.AddEmptyLine();

        // Show recent versions
        foreach (var version in recentVersions)
        {
            if (version == installedVersion)
                builder.AddLine($"[cyan1 bold]{Markup.Escape(version)}[/] [green]◄ installed[/]");
            else if (version == nugetData.Version)
                builder.AddLine($"[yellow]{Markup.Escape(version)}[/] [grey50](latest)[/]");
            else
                builder.AddLine($"[grey70]{Markup.Escape(version)}[/]");
        }

        // If installed version is not in recent versions, show it separately
        if (!installedInRecent && totalCount > MaxRecentVersions)
        {
            builder.AddEmptyLine();
            builder.AddLine("[grey50]─────────────[/]");
            builder.AddLine($"[cyan1 bold]{Markup.Escape(installedVersion)}[/] [green]◄ installed[/]");
            builder.AddLine("[grey50]─────────────[/]");
        }

        // Show count of older versions
        if (totalCount > MaxRecentVersions)
        {
            var olderCount = totalCount - MaxRecentVersions;
            builder.AddEmptyLine();
            builder.AddLine($"[grey50]... and {olderCount} older version{(olderCount == 1 ? "" : "s")}[/]");
        }

        builder.AddEmptyLine();
        return builder.WithMargin(1, 0, 0, 0).Build();
    }

    private static MarkupControl BuildWhatsNewPanel(NuGetPackage nugetData, PackageReference package)
    {
        var builder = Controls.Markup();

        // Version comparison
        if (package.IsOutdated && !string.IsNullOrEmpty(package.LatestVersion))
        {
            builder.AddLine($"[grey70 bold]Version Comparison:[/]");
            builder.AddLine($"[grey70]Installed:[/] [yellow]{Markup.Escape(package.Version)}[/]");
            builder.AddLine($"[grey70]Latest:[/]    [green]{Markup.Escape(package.LatestVersion)}[/]");
            builder.AddEmptyLine();

            // Simple breaking change heuristic based on semver major version diff
            var breakingChange = DetectPotentialBreakingChange(package.Version, package.LatestVersion);
            if (breakingChange)
            {
                builder.AddLine($"[red bold]⚠ Potential Breaking Change[/]");
                builder.AddLine($"[yellow]Major version changed. Review changelog before updating.[/]");
                builder.AddEmptyLine();
            }

            // Count intermediate versions
            var intermediateCount = CountIntermediateVersions(nugetData.Versions, package.Version, package.LatestVersion);
            if (intermediateCount > 0)
            {
                builder.AddLine($"[grey70]{intermediateCount} version(s) between installed and latest[/]");
                builder.AddEmptyLine();
            }
        }
        else
        {
            builder.AddLine($"[green bold]✓ You are on the latest version[/]");
            builder.AddEmptyLine();
        }

        // Release notes
        if (!string.IsNullOrEmpty(nugetData.ReleaseNotes))
        {
            builder.AddLine($"[grey70 bold]Release Notes:[/]");
            builder.AddLine($"[grey70]{Markup.Escape(nugetData.ReleaseNotes)}[/]");
        }
        else
        {
            builder.AddLine($"[grey50]No release notes available for this version.[/]");
        }

        builder.AddEmptyLine();
        return builder.WithMargin(1, 0, 0, 0).Build();
    }


    internal static bool DetectPotentialBreakingChange(string installedVersion, string latestVersion)
    {
        var installedMajor = GetMajorVersion(installedVersion);
        var latestMajor = GetMajorVersion(latestVersion);
        return installedMajor >= 0 && latestMajor >= 0 && latestMajor > installedMajor;
    }

    internal static int GetMajorVersion(string version)
    {
        if (string.IsNullOrEmpty(version)) return -1;
        var dotIndex = version.IndexOf('.');
        var majorStr = dotIndex >= 0 ? version.Substring(0, dotIndex) : version;
        return int.TryParse(majorStr, out var major) ? major : -1;
    }

    internal static int CountIntermediateVersions(List<string> versions, string installed, string latest)
    {
        var installedIdx = versions.IndexOf(installed);
        var latestIdx = versions.IndexOf(latest);
        if (installedIdx < 0 || latestIdx < 0) return 0;

        // Versions list is typically newest-first
        var low = Math.Min(installedIdx, latestIdx);
        var high = Math.Max(installedIdx, latestIdx);
        return Math.Max(0, high - low - 1);
    }

    private static IWindowControl BuildActionToolbar(
        PackageReference package,
        NuGetPackage? nugetData,
        Action onUpdate,
        Action onChangeVersion,
        Action onRemove,
        Action? onDeps)
    {
        bool hasVersions = nugetData?.Versions.Any() == true;

        // Update button
        var updateBtn = Controls.Button(package.IsOutdated ? "[cyan1]Update[/] [grey78](Ctrl+U)[/]" : "[grey50]Up to Date[/]")
            .OnClick((s, e) => onUpdate())
            .Enabled(package.IsOutdated)
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .WithDisabledBackgroundColor(Color.Grey23)
            .WithDisabledForegroundColor(Color.Grey50)
            .Build();

        // Select Version button
        var versionBtn = Controls.Button("[cyan1]Select Version[/] [grey78](Ctrl+V)[/]")
            .OnClick((s, e) => onChangeVersion())
            .Enabled(hasVersions)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .WithDisabledBackgroundColor(Color.Grey23)
            .WithDisabledForegroundColor(Color.Grey50)
            .Build();

        // Deps button
        var depsBtn = Controls.Button("[cyan1]Deps[/] [grey78](Ctrl+D)[/]")
            .OnClick((s, e) => onDeps?.Invoke())
            .Enabled(onDeps != null)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .WithDisabledBackgroundColor(Color.Grey23)
            .WithDisabledForegroundColor(Color.Grey50)
            .Build();

        // Remove button
        var removeBtn = Controls.Button("[cyan1]Remove[/] [grey78](Ctrl+X)[/]")
            .OnClick((s, e) => onRemove())
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .WithDisabledBackgroundColor(Color.Grey23)
            .WithDisabledForegroundColor(Color.Grey50)
            .Build();

        return Controls.Toolbar()
            .AddButton(updateBtn)
            .AddButton(versionBtn)
            .AddButton(depsBtn)
            .AddButton(removeBtn)
            .WithSpacing(2)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(1, 0, 1, 0)
            .Build();
    }

    internal static string FormatDownloads(long downloads)
    {
        if (downloads >= 1_000_000_000)
            return $"{downloads / 1_000_000_000.0:F1}B";
        if (downloads >= 1_000_000)
            return $"{downloads / 1_000_000.0:F1}M";
        if (downloads >= 1_000)
            return $"{downloads / 1_000.0:F1}K";
        return downloads.ToString();
    }

    internal static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824)
            return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:F2} MB";
        if (bytes >= 1_024)
            return $"{bytes / 1_024.0:F2} KB";
        return $"{bytes} bytes";
    }

}
