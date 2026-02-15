using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Tests.Helpers;
using LazyNuGet.UI.Components;

namespace LazyNuGet.Tests.UI;

public class ProjectDashboardBuilderTests
{
    [Fact]
    public void BuildDashboard_ContainsProjectName()
    {
        var project = SampleDataBuilder.CreateProjectInfo(name: "MyApp");
        var lines = ProjectDashboardBuilder.BuildDashboard(project);
        lines.Should().Contain(l => l.Contains("MyApp"));
    }

    [Fact]
    public void BuildDashboard_ContainsFramework()
    {
        var project = SampleDataBuilder.CreateProjectInfo(targetFramework: "net9.0");
        var lines = ProjectDashboardBuilder.BuildDashboard(project);
        lines.Should().Contain(l => l.Contains("net9.0"));
    }

    [Fact]
    public void BuildDashboard_ContainsStatsCard()
    {
        var project = SampleDataBuilder.CreateProjectInfo();
        var lines = ProjectDashboardBuilder.BuildDashboard(project);
        lines.Should().Contain(l => l.Contains("Total"));
    }

    [Fact]
    public void BuildDashboard_ShowsTotalPackageCount()
    {
        var packages = new List<PackageReference>
        {
            SampleDataBuilder.CreatePackageReference(),
            SampleDataBuilder.CreatePackageReference(id: "Serilog")
        };
        var project = SampleDataBuilder.CreateProjectInfo(packages: packages);
        var lines = ProjectDashboardBuilder.BuildDashboard(project);

        // Stats card should show count 2
        var allText = string.Join("\n", lines);
        allText.Should().Contain("2");
    }

    [Fact]
    public void BuildDashboard_WithOutdatedPackages_ShowsNeedsAttention()
    {
        var project = SampleDataBuilder.CreateProjectInfo();
        var outdated = new List<PackageReference>
        {
            SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "2.0.0")
        };
        var lines = ProjectDashboardBuilder.BuildDashboard(project, outdated);
        lines.Should().Contain(l => l.Contains("Needs Attention"));
    }

    [Fact]
    public void BuildDashboard_NoOutdated_ShowsAllUpToDate()
    {
        var packages = new List<PackageReference>
        {
            SampleDataBuilder.CreatePackageReference(latestVersion: null)
        };
        var project = SampleDataBuilder.CreateProjectInfo(packages: packages);
        var lines = ProjectDashboardBuilder.BuildDashboard(project, new List<PackageReference>());
        lines.Should().Contain(l => l.Contains("up-to-date"));
    }

    [Fact]
    public void BuildDashboard_ManyOutdated_ShowsAndMore()
    {
        var project = SampleDataBuilder.CreateProjectInfo();
        var outdated = Enumerable.Range(0, 8)
            .Select(i => SampleDataBuilder.CreatePackageReference(
                id: $"Pkg{i}", version: "1.0.0", latestVersion: "2.0.0"))
            .ToList();

        var lines = ProjectDashboardBuilder.BuildDashboard(project, outdated);
        lines.Should().Contain(l => l.Contains("more outdated"));
    }

    [Fact]
    public void BuildDashboard_ManyPackages_ShowsAndMore()
    {
        var packages = Enumerable.Range(0, 8)
            .Select(i => SampleDataBuilder.CreatePackageReference(id: $"Package{i}"))
            .ToList();
        var project = SampleDataBuilder.CreateProjectInfo(packages: packages);

        var lines = ProjectDashboardBuilder.BuildDashboard(project);
        lines.Should().Contain(l => l.Contains("more"));
    }

    [Fact]
    public void BuildDashboard_ShowsQuickActions()
    {
        var project = SampleDataBuilder.CreateProjectInfo();
        var lines = ProjectDashboardBuilder.BuildDashboard(project);
        lines.Should().Contain(l => l.Contains("Quick Actions"));
    }

    [Fact]
    public void BuildDashboard_WithOutdated_ShowsUpdateAllAction()
    {
        var project = SampleDataBuilder.CreateProjectInfo();
        var outdated = new List<PackageReference>
        {
            SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "2.0.0")
        };
        var lines = ProjectDashboardBuilder.BuildDashboard(project, outdated);
        lines.Should().Contain(l => l.Contains("Ctrl+U"));
    }

    [Fact]
    public void BuildDashboard_ShowsRestoreAction()
    {
        var project = SampleDataBuilder.CreateProjectInfo();
        var lines = ProjectDashboardBuilder.BuildDashboard(project);
        lines.Should().Contain(l => l.Contains("Ctrl+R"));
    }

    // --- ShortenPath (internal) ---

    [Fact]
    public void ShortenPath_ShortPath_ReturnsUnchanged()
    {
        var path = Path.Combine("src", "App.csproj");
        ProjectDashboardBuilder.ShortenPath(path).Should().Be(path);
    }

    [Fact]
    public void ShortenPath_LongPath_ReturnsShortened()
    {
        var path = Path.Combine("home", "user", "source", "myproject", "src", "App.csproj");
        var result = ProjectDashboardBuilder.ShortenPath(path);
        result.Should().StartWith("...");
        result.Should().Contain("src");
        result.Should().Contain("App.csproj");
    }

    [Fact]
    public void ShortenPath_EmptyPath_ReturnsEmpty()
    {
        ProjectDashboardBuilder.ShortenPath("").Should().Be("");
    }

    [Fact]
    public void InteractiveDashboardBuilder_ShortenPath_Works()
    {
        var path = Path.Combine("home", "user", "source", "project", "src", "App.csproj");
        var result = InteractiveDashboardBuilder.ShortenPath(path);
        result.Should().StartWith("...");
    }
}
