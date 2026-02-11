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
/// Builder for interactive package details with real button controls
/// </summary>
public static class InteractivePackageDetailsBuilder
{
    public static List<IWindowControl> BuildInteractiveDetails(
        PackageReference package,
        NuGetPackage? nugetData,
        Action onUpdate,
        Action onChangeVersion,
        Action onRemove)
    {
        var controls = new List<IWindowControl>();

        // Package header
        var header = Controls.Markup()
            .AddLine($"[cyan1 bold]Package: {Markup.Escape(package.Id)}[/]")
            .AddLine($"[grey70]Installed: {package.Version}[/]")
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
        var toolbar = BuildActionToolbar(package, nugetData, onUpdate, onChangeVersion, onRemove);
        controls.Add(toolbar);

        // Empty markup below toolbar for background extension
        var toolbarBottom = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(1, 0, 1, 0)
            .Build();
        controls.Add(toolbarBottom);

        // Separator after toolbar (always visible)
        var separator2 = Controls.Rule();
        separator2.Margin = new Margin(1, 0, 1, 1);
        controls.Add(separator2);

        // NuGet.org data section
        if (nugetData != null)
        {
            var details = BuildNuGetDetails(nugetData, package);
            controls.Add(details);
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
            builder.AddLine($"[grey70]Latest Version: {package.LatestVersion}[/]");
            builder.AddEmptyLine();
        }
        else if (!package.IsOutdated)
        {
            builder.AddLine($"[green bold]✓ Up to date[/]");
            builder.AddEmptyLine();
        }

        return builder.WithMargin(1, 0, 0, 0).Build();
    }

    private static MarkupControl BuildNuGetDetails(NuGetPackage nugetData, PackageReference package)
    {
        var builder = Controls.Markup();

        // Verification and warning badges
        var badges = new List<string>();
        if (nugetData.IsVerified)
        {
            badges.Add("[green]✓ Verified[/]");
        }
        if (nugetData.VulnerabilityCount > 0)
        {
            badges.Add($"[red]⚠ {nugetData.VulnerabilityCount} Vulnerabilities[/]");
        }
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
            {
                builder.AddLine($"[yellow]{Markup.Escape(nugetData.DeprecationMessage)}[/]");
            }
            if (!string.IsNullOrEmpty(nugetData.AlternatePackageId))
            {
                builder.AddLine($"[grey70]Alternative: {Markup.Escape(nugetData.AlternatePackageId)}[/]");
            }
            builder.AddEmptyLine();
        }

        if (!string.IsNullOrEmpty(nugetData.Description))
        {
            builder.AddLine($"[grey70 bold]Description:[/]");
            builder.AddLine($"[grey70]{Markup.Escape(nugetData.Description)}[/]");
            builder.AddEmptyLine();
        }

        if (nugetData.Authors.Any())
        {
            var authors = string.Join(", ", nugetData.Authors);
            builder.AddLine($"[grey70]Authors: {Markup.Escape(authors)}[/]");
        }

        // License - prefer expression over URL
        if (!string.IsNullOrEmpty(nugetData.LicenseExpression))
        {
            builder.AddLine($"[grey70]License: {Markup.Escape(nugetData.LicenseExpression)}[/]");
        }
        else if (!string.IsNullOrEmpty(nugetData.LicenseUrl))
        {
            builder.AddLine($"[grey70]License: {Markup.Escape(nugetData.LicenseUrl)}[/]");
        }

        if (nugetData.Tags.Any())
        {
            var tags = string.Join(", ", nugetData.Tags);
            builder.AddLine($"[grey70]Tags: {Markup.Escape(tags)}[/]");
        }

        if (nugetData.TotalDownloads > 0)
        {
            builder.AddLine($"[grey70]Downloads: {FormatDownloads(nugetData.TotalDownloads)}[/]");
        }

        if (nugetData.PackageSize.HasValue)
        {
            builder.AddLine($"[grey70]Package Size: {FormatSize(nugetData.PackageSize.Value)}[/]");
        }

        if (nugetData.Published.HasValue)
        {
            builder.AddLine($"[grey70]Published: {nugetData.Published.Value:yyyy-MM-dd}[/]");
        }

        if (nugetData.TargetFrameworks.Any())
        {
            var frameworks = string.Join(", ", nugetData.TargetFrameworks);
            builder.AddLine($"[grey70]Target Frameworks: {Markup.Escape(frameworks)}[/]");
        }

        if (!string.IsNullOrEmpty(nugetData.ProjectUrl))
        {
            builder.AddLine($"[grey70]Project URL: {Markup.Escape(nugetData.ProjectUrl)}[/]");
        }

        if (!string.IsNullOrEmpty(nugetData.RepositoryUrl))
        {
            builder.AddLine($"[grey70]Repository: {Markup.Escape(nugetData.RepositoryUrl)}[/]");
        }

        // Release notes
        if (!string.IsNullOrEmpty(nugetData.ReleaseNotes))
        {
            builder.AddEmptyLine();
            builder.AddLine($"[grey70 bold]Release Notes:[/]");
            // Truncate release notes if too long
            var notes = nugetData.ReleaseNotes;
            if (notes.Length > 300)
            {
                notes = notes.Substring(0, 297) + "...";
            }
            builder.AddLine($"[grey70]{Markup.Escape(notes)}[/]");
        }

        if (nugetData.Versions.Any())
        {
            builder.AddEmptyLine();
            builder.AddLine($"[grey70 bold]Available Versions:[/]");
            foreach (var version in nugetData.Versions.Take(5))
            {
                var indicator = version == package.Version ? "◄ installed" : "";
                builder.AddLine($"[grey70]{version} {indicator}[/]");
            }
            if (nugetData.Versions.Count > 5)
            {
                builder.AddLine($"[grey50]... and {nugetData.Versions.Count - 5} more[/]");
            }
        }

        builder.AddEmptyLine();

        return builder.WithMargin(1, 0, 0, 0).Build();
    }

    private static IWindowControl BuildActionToolbar(
        PackageReference package,
        NuGetPackage? nugetData,
        Action onUpdate,
        Action onChangeVersion,
        Action onRemove)
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

        // Change Version button
        var versionBtn = Controls.Button("[cyan1]Version[/] [grey78](Ctrl+V)[/]")
            .OnClick((s, e) => onChangeVersion())
            .Enabled(hasVersions)
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
            .AddButton(removeBtn)
            .WithSpacing(2)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(1, 0, 1, 0)
            .Build();
    }

    private static string FormatDownloads(long downloads)
    {
        if (downloads >= 1_000_000_000)
            return $"{downloads / 1_000_000_000.0:F1}B";
        if (downloads >= 1_000_000)
            return $"{downloads / 1_000_000.0:F1}M";
        if (downloads >= 1_000)
            return $"{downloads / 1_000.0:F1}K";
        return downloads.ToString();
    }

    private static string FormatSize(long bytes)
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
