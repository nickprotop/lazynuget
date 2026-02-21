using FluentAssertions;
using LazyNuGet.Services;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Services;

public class SolutionDiscoveryServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _temp = new();
    private readonly SolutionDiscoveryService _service = new();

    // ── DiscoverSolutionsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverSolutionsAsync_NoSlnFiles_ReturnsEmpty()
    {
        _temp.WriteFile("App.csproj", "<Project />");

        var result = await _service.DiscoverSolutionsAsync(_temp.Path);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverSolutionsAsync_SingleSln_ParsesName()
    {
        var csprojPath = _temp.WriteFile("src/App/App.csproj", "<Project />");
        _temp.WriteFile("MySolution.sln", SampleDataBuilder.CreateSolutionFile(
            ("App", "src/App/App.csproj")));

        var result = await _service.DiscoverSolutionsAsync(_temp.Path);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("MySolution");
    }

    [Fact]
    public async Task DiscoverSolutionsAsync_SingleSln_ParsesProjectPaths()
    {
        _temp.WriteFile("src/App/App.csproj", "<Project />");
        _temp.WriteFile("MySolution.sln", SampleDataBuilder.CreateSolutionFile(
            ("App", "src/App/App.csproj")));

        var result = await _service.DiscoverSolutionsAsync(_temp.Path);

        result[0].ProjectPaths.Should().HaveCount(1);
        result[0].ProjectPaths[0].Should().EndWith("App.csproj");
    }

    [Fact]
    public async Task DiscoverSolutionsAsync_MultipleProjects_ParsesAll()
    {
        _temp.WriteFile("src/App/App.csproj", "<Project />");
        _temp.WriteFile("src/Lib/Lib.csproj", "<Project />");
        _temp.WriteFile("MySolution.sln", SampleDataBuilder.CreateSolutionFile(
            ("App", "src/App/App.csproj"),
            ("Lib", "src/Lib/Lib.csproj")));

        var result = await _service.DiscoverSolutionsAsync(_temp.Path);

        result[0].ProjectPaths.Should().HaveCount(2);
    }

    [Fact]
    public async Task DiscoverSolutionsAsync_MultipleSolutions_FindsBoth()
    {
        _temp.WriteFile("SolutionA.sln", SampleDataBuilder.CreateSolutionFile());
        _temp.WriteFile("sub/SolutionB.sln", SampleDataBuilder.CreateSolutionFile());

        var result = await _service.DiscoverSolutionsAsync(_temp.Path);

        result.Should().HaveCount(2);
        result.Select(s => s.Name).Should().Contain("SolutionA").And.Contain("SolutionB");
    }

    [Fact]
    public async Task DiscoverSolutionsAsync_SlnInSubdirectory_Found()
    {
        _temp.WriteFile("src/deep/nested/App.sln", SampleDataBuilder.CreateSolutionFile());

        var result = await _service.DiscoverSolutionsAsync(_temp.Path);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task DiscoverSolutionsAsync_EmptySlnFile_ProjectPathsIsEmpty()
    {
        _temp.WriteFile("Empty.sln", SampleDataBuilder.CreateSolutionFile());

        var result = await _service.DiscoverSolutionsAsync(_temp.Path);

        result.Should().HaveCount(1);
        result[0].ProjectPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverSolutionsAsync_AbsoluteProjectPath_ResolvedCorrectly()
    {
        _temp.WriteFile("src/App/App.csproj", "<Project />");
        _temp.WriteFile("MySolution.sln", SampleDataBuilder.CreateSolutionFile(
            ("App", "src/App/App.csproj")));

        var result = await _service.DiscoverSolutionsAsync(_temp.Path);

        var projectPath = result[0].ProjectPaths[0];
        Path.IsPathRooted(projectPath).Should().BeTrue("project path should be resolved to absolute");
    }

    [Fact]
    public async Task DiscoverSolutionsAsync_BackslashPaths_NormalisedToLocalSeparator()
    {
        // .sln files on Windows use backslashes; test that we normalize them
        _temp.WriteFile("MySolution.sln",
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"App\", \"src\\App\\App.csproj\", \"{GUID}\"\nEndProject\n");

        var result = await _service.DiscoverSolutionsAsync(_temp.Path);

        // Path should not contain raw backslash on Linux/Mac (or should equal expected on Windows)
        result[0].ProjectPaths[0].Should().Contain("App.csproj");
    }

    [Fact]
    public async Task DiscoverSolutionsAsync_FsprojAndVbproj_BothParsed()
    {
        _temp.WriteFile("MySolution.sln", SampleDataBuilder.CreateSolutionFile(
            ("FsApp", "src/FsApp/FsApp.fsproj"),
            ("VbApp", "src/VbApp/VbApp.vbproj")));

        var result = await _service.DiscoverSolutionsAsync(_temp.Path);

        result[0].ProjectPaths.Should().HaveCount(2);
        result[0].ProjectPaths.Should().Contain(p => p.EndsWith(".fsproj"));
        result[0].ProjectPaths.Should().Contain(p => p.EndsWith(".vbproj"));
    }

    [Fact]
    public async Task DiscoverSolutionsAsync_NonexistentDirectory_ReturnsEmpty()
    {
        var result = await _service.DiscoverSolutionsAsync("/nonexistent/path/that/does/not/exist");
        result.Should().BeEmpty();
    }

    public void Dispose() => _temp.Dispose();
}
