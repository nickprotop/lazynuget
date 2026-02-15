using FluentAssertions;
using LazyNuGet.Services;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Services;

public class ProjectParserServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _temp = new();
    private readonly ProjectParserService _service = new();

    [Fact]
    public async Task ParseProjectAsync_ValidProject_ReturnsProjectInfo()
    {
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0",
            ("Newtonsoft.Json", "13.0.3"));
        var filePath = _temp.WriteFile("TestProject.csproj", csproj);

        var result = await _service.ParseProjectAsync(filePath);

        result.Should().NotBeNull();
        result!.Name.Should().Be("TestProject");
        result.TargetFramework.Should().Be("net9.0");
        result.FilePath.Should().Be(filePath);
    }

    [Fact]
    public async Task ParseProjectAsync_ExtractsPackages()
    {
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0",
            ("Newtonsoft.Json", "13.0.3"),
            ("Serilog", "3.1.1"));
        var filePath = _temp.WriteFile("WithPkgs.csproj", csproj);

        var result = await _service.ParseProjectAsync(filePath);

        result!.Packages.Should().HaveCount(2);
        result.Packages[0].Id.Should().Be("Newtonsoft.Json");
        result.Packages[0].Version.Should().Be("13.0.3");
        result.Packages[1].Id.Should().Be("Serilog");
    }

    [Fact]
    public async Task ParseProjectAsync_SetsFilePath()
    {
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0");
        var filePath = _temp.WriteFile("PathTest.csproj", csproj);

        var result = await _service.ParseProjectAsync(filePath);

        result!.FilePath.Should().Be(filePath);
    }

    [Fact]
    public async Task ParseProjectAsync_InvalidFile_ReturnsNull()
    {
        var filePath = _temp.WriteFile("Invalid.csproj", "not xml");

        var result = await _service.ParseProjectAsync(filePath);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ParseProjectAsync_MissingFile_ReturnsNull()
    {
        var result = await _service.ParseProjectAsync("/nonexistent/Missing.csproj");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ParseProjectAsync_EmptyPackages_ReturnsEmptyList()
    {
        var csproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
</Project>";
        var filePath = _temp.WriteFile("NoPkgs.csproj", csproj);

        var result = await _service.ParseProjectAsync(filePath);

        result!.Packages.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseProjectAsync_SetsLastModified()
    {
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0");
        var filePath = _temp.WriteFile("ModTime.csproj", csproj);

        var result = await _service.ParseProjectAsync(filePath);

        result!.LastModified.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(5));
    }

    public void Dispose() => _temp.Dispose();
}
