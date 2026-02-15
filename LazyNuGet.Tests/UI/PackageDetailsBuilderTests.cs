using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Tests.Helpers;
using LazyNuGet.UI.Components;

namespace LazyNuGet.Tests.UI;

public class PackageDetailsBuilderTests
{
    [Fact]
    public void BuildDetails_ContainsPackageId()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(id: "Newtonsoft.Json");
        var lines = PackageDetailsBuilder.BuildDetails(pkg);
        lines.Should().Contain(l => l.Contains("Newtonsoft.Json"));
    }

    [Fact]
    public void BuildDetails_ContainsInstalledVersion()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(version: "13.0.1");
        var lines = PackageDetailsBuilder.BuildDetails(pkg);
        lines.Should().Contain(l => l.Contains("13.0.1"));
    }

    [Fact]
    public void BuildDetails_Vulnerable_ShowsWarning()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(hasVulnerability: true);
        var lines = PackageDetailsBuilder.BuildDetails(pkg);
        lines.Should().Contain(l => l.Contains("VULNERABILITY"));
    }

    [Fact]
    public void BuildDetails_Outdated_ShowsUpdateAvailable()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "2.0.0");
        var lines = PackageDetailsBuilder.BuildDetails(pkg);
        lines.Should().Contain(l => l.Contains("Update Available"));
    }

    [Fact]
    public void BuildDetails_UpToDate_ShowsGreen()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(latestVersion: null);
        var lines = PackageDetailsBuilder.BuildDetails(pkg);
        lines.Should().Contain(l => l.Contains("Up to date"));
    }

    [Fact]
    public void BuildDetails_WithNuGetData_ShowsDescription()
    {
        var pkg = SampleDataBuilder.CreatePackageReference();
        var nugetData = SampleDataBuilder.CreateNuGetPackage(description: "A great library");
        var lines = PackageDetailsBuilder.BuildDetails(pkg, nugetData);
        lines.Should().Contain(l => l.Contains("A great library"));
    }

    [Fact]
    public void BuildDetails_WithNuGetData_ShowsDownloads()
    {
        var pkg = SampleDataBuilder.CreatePackageReference();
        var nugetData = SampleDataBuilder.CreateNuGetPackage(totalDownloads: 5_000_000);
        var lines = PackageDetailsBuilder.BuildDetails(pkg, nugetData);
        lines.Should().Contain(l => l.Contains("Downloads"));
    }

    [Fact]
    public void BuildDetails_WithNuGetData_ShowsVersions()
    {
        var pkg = SampleDataBuilder.CreatePackageReference();
        var nugetData = SampleDataBuilder.CreateNuGetPackage(versions: new List<string> { "2.0.0", "1.0.0" });
        var lines = PackageDetailsBuilder.BuildDetails(pkg, nugetData);
        lines.Should().Contain(l => l.Contains("Available Versions"));
    }

    [Fact]
    public void BuildDetails_WithoutNuGetData_ShowsLoading()
    {
        var pkg = SampleDataBuilder.CreatePackageReference();
        var lines = PackageDetailsBuilder.BuildDetails(pkg, null);
        lines.Should().Contain(l => l.Contains("Loading"));
    }

    [Fact]
    public void BuildDetails_Outdated_ShowsUpdateAction()
    {
        var pkg = SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "2.0.0");
        var lines = PackageDetailsBuilder.BuildDetails(pkg);
        lines.Should().Contain(l => l.Contains("Ctrl+U"));
    }

    [Fact]
    public void BuildDetails_ShowsRemoveAction()
    {
        var pkg = SampleDataBuilder.CreatePackageReference();
        var lines = PackageDetailsBuilder.BuildDetails(pkg);
        lines.Should().Contain(l => l.Contains("Ctrl+X"));
    }

    // --- FormatDownloads (internal) ---

    [Fact]
    public void FormatDownloads_Thousands()
    {
        PackageDetailsBuilder.FormatDownloads(5_000).Should().Be("5.0K");
    }

    [Fact]
    public void FormatDownloads_Millions()
    {
        PackageDetailsBuilder.FormatDownloads(3_500_000).Should().Be("3.5M");
    }

    [Fact]
    public void FormatDownloads_Billions()
    {
        PackageDetailsBuilder.FormatDownloads(2_000_000_000).Should().Be("2.0B");
    }

    [Fact]
    public void FormatDownloads_BelowThousand()
    {
        PackageDetailsBuilder.FormatDownloads(42).Should().Be("42");
    }

    // --- WrapText (internal) ---

    [Fact]
    public void WrapText_ShortText_ReturnsUnchanged()
    {
        PackageDetailsBuilder.WrapText("hello", 50).Should().Be("hello");
    }

    [Fact]
    public void WrapText_LongText_TruncatesWithEllipsis()
    {
        var longText = new string('a', 100);
        var result = PackageDetailsBuilder.WrapText(longText, 50);
        result.Should().HaveLength(53); // 50 + "..."
        result.Should().EndWith("...");
    }
}
