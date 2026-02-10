using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.Models;
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

        // Interactive Action Buttons - Title + Real buttons!
        var actionTitle = BuildActionButtons(package, onUpdate, onRemove);
        controls.Add(actionTitle);

        // Add actual button controls
        var buttons = GetActionButtons(package, onUpdate, onRemove);
        controls.AddRange(buttons);

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

        if (!string.IsNullOrEmpty(nugetData.Description))
        {
            builder.AddLine($"[grey70 bold]Description:[/]");
            builder.AddLine($"[grey70]{Markup.Escape(WrapText(nugetData.Description, 50))}[/]");
            builder.AddEmptyLine();
        }

        if (nugetData.TotalDownloads > 0)
        {
            builder.AddLine($"[grey70]Downloads: {FormatDownloads(nugetData.TotalDownloads)}[/]");
        }

        if (nugetData.Published.HasValue)
        {
            builder.AddLine($"[grey70]Published: {nugetData.Published.Value:yyyy-MM-dd}[/]");
        }

        if (!string.IsNullOrEmpty(nugetData.ProjectUrl))
        {
            builder.AddLine($"[grey70]Project URL: {Markup.Escape(nugetData.ProjectUrl)}[/]");
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

    private static IWindowControl BuildActionButtons(
        PackageReference package,
        Action onUpdate,
        Action onRemove)
    {
        // Title
        return Controls.Markup()
            .AddLine("[cyan1 bold]Actions:[/]")
            .AddLine("")
            .AddLine("[grey70]Click buttons or use keyboard shortcuts:[/]")
            .WithMargin(1, 0, 0, 1)
            .Build();
    }

    public static List<IWindowControl> GetActionButtons(
        PackageReference package,
        Action onUpdate,
        Action onRemove)
    {
        var buttons = new List<IWindowControl>();

        // Update button
        buttons.Add(Controls.Button(package.IsOutdated ? "⚡ Update to Latest (Ctrl+U)" : "✓ Up to Date")
            .WithAlignment(HorizontalAlignment.Left)
            .WithMargin(1, 0, 0, 0)
            .OnClick((s, e) => onUpdate())
            .Enabled(package.IsOutdated)
            .Build());

        // Remove button
        buttons.Add(Controls.Button("🗑 Remove Package (Ctrl+X)")
            .WithAlignment(HorizontalAlignment.Left)
            .WithMargin(1, 0, 0, 1)
            .OnClick((s, e) => onRemove())
            .Build());

        return buttons;
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

    private static string WrapText(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;
        return text.Substring(0, maxLength) + "...";
    }
}
