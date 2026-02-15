using FluentAssertions;
using LazyNuGet.Services;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Services;

public class ProjectDiscoveryServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _temp = new();
    private readonly ProjectDiscoveryService _service = new();

    [Fact]
    public async Task DiscoverProjectsAsync_FindsAllProjectTypes()
    {
        _temp.WriteFile("App.csproj", "<Project />");
        _temp.WriteFile("Lib.fsproj", "<Project />");
        _temp.WriteFile("Legacy.vbproj", "<Project />");

        var projects = await _service.DiscoverProjectsAsync(_temp.Path);
        projects.Should().HaveCount(3);
    }

    [Fact]
    public async Task DiscoverProjectsAsync_ExcludesBinDirectory()
    {
        _temp.WriteFile("src/App.csproj", "<Project />");
        _temp.WriteFile("src/bin/Debug/App.csproj", "<Project />");

        var projects = await _service.DiscoverProjectsAsync(_temp.Path);
        projects.Should().HaveCount(1);
        projects[0].Should().NotContain($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");
    }

    [Fact]
    public async Task DiscoverProjectsAsync_ExcludesObjDirectory()
    {
        _temp.WriteFile("src/App.csproj", "<Project />");
        _temp.WriteFile("src/obj/Debug/App.csproj", "<Project />");

        var projects = await _service.DiscoverProjectsAsync(_temp.Path);
        projects.Should().HaveCount(1);
        projects[0].Should().NotContain($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");
    }

    [Fact]
    public async Task DiscoverProjectsAsync_SortsAlphabetically()
    {
        _temp.WriteFile("Zebra.csproj", "<Project />");
        _temp.WriteFile("Alpha.csproj", "<Project />");
        _temp.WriteFile("Middle.csproj", "<Project />");

        var projects = await _service.DiscoverProjectsAsync(_temp.Path);
        projects.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task DiscoverProjectsAsync_EmptyDirectory_ReturnsEmpty()
    {
        var projects = await _service.DiscoverProjectsAsync(_temp.Path);
        projects.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverProjectsAsync_NonexistentDirectory_ReturnsEmpty()
    {
        var projects = await _service.DiscoverProjectsAsync("/nonexistent/path/does/not/exist");
        projects.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverProjectsAsync_FindsNestedProjects()
    {
        _temp.WriteFile("src/App1/App1.csproj", "<Project />");
        _temp.WriteFile("src/App2/App2.csproj", "<Project />");
        _temp.WriteFile("tests/App1.Tests/App1.Tests.csproj", "<Project />");

        var projects = await _service.DiscoverProjectsAsync(_temp.Path);
        projects.Should().HaveCount(3);
    }

    [Fact]
    public async Task DiscoverProjectsAsync_IgnoresNonProjectFiles()
    {
        _temp.WriteFile("App.csproj", "<Project />");
        _temp.WriteFile("readme.md", "# Readme");
        _temp.WriteFile("app.sln", "solution file");

        var projects = await _service.DiscoverProjectsAsync(_temp.Path);
        projects.Should().HaveCount(1);
    }

    public void Dispose() => _temp.Dispose();
}
