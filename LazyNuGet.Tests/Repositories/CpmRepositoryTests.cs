using FluentAssertions;
using LazyNuGet.Repositories;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Repositories;

public class CpmRepositoryTests : IDisposable
{
    private readonly TempDirectoryFixture _temp = new();
    private readonly CpmRepository _repo = new();

    // ── FindPropsFile ─────────────────────────────────────────────────────────

    [Fact]
    public void FindPropsFile_PropsInSameDir_Found()
    {
        _temp.WriteFile("Directory.Packages.props", SampleDataBuilder.CreatePropsFile());
        var csproj = _temp.WriteFile("MyProject.csproj", "<Project />");

        var result = CpmRepository.FindPropsFile(csproj);

        result.Should().Be(Path.Combine(_temp.Path, "Directory.Packages.props"));
    }

    [Fact]
    public void FindPropsFile_PropsInParentDir_Found()
    {
        _temp.WriteFile("Directory.Packages.props", SampleDataBuilder.CreatePropsFile());
        var csproj = _temp.WriteFile("src/MyProject/MyProject.csproj", "<Project />");

        var result = CpmRepository.FindPropsFile(csproj);

        result.Should().NotBeNull();
        result!.Should().EndWith("Directory.Packages.props");
    }

    [Fact]
    public void FindPropsFile_NoPropsFile_ReturnsNull()
    {
        var csproj = _temp.WriteFile("MyProject.csproj", "<Project />");

        var result = CpmRepository.FindPropsFile(csproj);

        result.Should().BeNull();
    }

    [Fact]
    public void FindPropsFile_PropsInSameDirTakesPrecedence_OverParent()
    {
        // Parent has a props file, and so does the immediate directory
        _temp.WriteFile("Directory.Packages.props", SampleDataBuilder.CreatePropsFile(("OldPkg", "1.0.0")));
        var subPropsPath = _temp.WriteFile("src/Directory.Packages.props", SampleDataBuilder.CreatePropsFile(("NewPkg", "2.0.0")));
        var csproj = _temp.WriteFile("src/MyProject.csproj", "<Project />");

        var result = CpmRepository.FindPropsFile(csproj);

        result.Should().Be(subPropsPath);
    }

    // ── ReadPackageVersionsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task ReadPackageVersionsAsync_AttributeStyle_ParsesVersions()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(
                ("Newtonsoft.Json", "13.0.3"),
                ("Serilog", "3.1.1")));

        var result = await _repo.ReadPackageVersionsAsync(propsPath);

        result.Should().HaveCount(2);
        result["Newtonsoft.Json"].Should().Be("13.0.3");
        result["Serilog"].Should().Be("3.1.1");
    }

    [Fact]
    public async Task ReadPackageVersionsAsync_CaseInsensitive_Lookup()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("Newtonsoft.Json", "13.0.3")));

        var result = await _repo.ReadPackageVersionsAsync(propsPath);

        result.ContainsKey("newtonsoft.json").Should().BeTrue();
        result.ContainsKey("NEWTONSOFT.JSON").Should().BeTrue();
    }

    [Fact]
    public async Task ReadPackageVersionsAsync_ElementStyleVersion_ParsesVersion()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props", @"<Project>
  <ItemGroup>
    <PackageVersion Include=""MyPkg"">
      <Version>5.0.0</Version>
    </PackageVersion>
  </ItemGroup>
</Project>");

        var result = await _repo.ReadPackageVersionsAsync(propsPath);

        result["MyPkg"].Should().Be("5.0.0");
    }

    [Fact]
    public async Task ReadPackageVersionsAsync_EmptyFile_ReturnsEmpty()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props", "<Project><ItemGroup /></Project>");

        var result = await _repo.ReadPackageVersionsAsync(propsPath);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadPackageVersionsAsync_NonexistentFile_ReturnsEmpty()
    {
        var result = await _repo.ReadPackageVersionsAsync("/nonexistent/path/Directory.Packages.props");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadPackageVersionsAsync_MalformedXml_ReturnsEmpty()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props", "not xml <<<<");

        var result = await _repo.ReadPackageVersionsAsync(propsPath);

        result.Should().BeEmpty();
    }

    // ── UpdatePackageVersionAsync ─────────────────────────────────────────────

    [Fact]
    public async Task UpdatePackageVersionAsync_AttributeStyle_UpdatesVersion()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("Newtonsoft.Json", "13.0.1")));

        await _repo.UpdatePackageVersionAsync(propsPath, "Newtonsoft.Json", "13.0.3");

        var updated = await _repo.ReadPackageVersionsAsync(propsPath);
        updated["Newtonsoft.Json"].Should().Be("13.0.3");
    }

    [Fact]
    public async Task UpdatePackageVersionAsync_CaseInsensitive_PackageId()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("Newtonsoft.Json", "13.0.1")));

        await _repo.UpdatePackageVersionAsync(propsPath, "newtonsoft.json", "13.0.3");

        var updated = await _repo.ReadPackageVersionsAsync(propsPath);
        updated["Newtonsoft.Json"].Should().Be("13.0.3");
    }

    [Fact]
    public async Task UpdatePackageVersionAsync_ElementStyle_UpdatesVersion()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props", @"<Project>
  <ItemGroup>
    <PackageVersion Include=""MyPkg"">
      <Version>1.0.0</Version>
    </PackageVersion>
  </ItemGroup>
</Project>");

        await _repo.UpdatePackageVersionAsync(propsPath, "MyPkg", "2.0.0");

        var updated = await _repo.ReadPackageVersionsAsync(propsPath);
        updated["MyPkg"].Should().Be("2.0.0");
    }

    [Fact]
    public async Task UpdatePackageVersionAsync_PreservesOtherPackages()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(
                ("Newtonsoft.Json", "13.0.1"),
                ("Serilog", "3.1.1")));

        await _repo.UpdatePackageVersionAsync(propsPath, "Newtonsoft.Json", "13.0.3");

        var updated = await _repo.ReadPackageVersionsAsync(propsPath);
        updated["Serilog"].Should().Be("3.1.1");
        updated.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdatePackageVersionAsync_PackageNotFound_ThrowsInvalidOperation()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("OtherPkg", "1.0.0")));

        Func<Task> act = () => _repo.UpdatePackageVersionAsync(propsPath, "MissingPkg", "2.0.0");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*MissingPkg*");
    }

    // ── RemovePackageVersionAsync ─────────────────────────────────────────────

    [Fact]
    public async Task RemovePackageVersionAsync_ExistingPackage_RemovesEntry()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(
                ("Newtonsoft.Json", "13.0.3"),
                ("Serilog", "3.1.1")));

        await _repo.RemovePackageVersionAsync(propsPath, "Newtonsoft.Json");

        var updated = await _repo.ReadPackageVersionsAsync(propsPath);
        updated.Should().NotContainKey("Newtonsoft.Json");
        updated.Should().ContainKey("Serilog");
    }

    [Fact]
    public async Task RemovePackageVersionAsync_CaseInsensitive_PackageId()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("Newtonsoft.Json", "13.0.3")));

        await _repo.RemovePackageVersionAsync(propsPath, "newtonsoft.json");

        var updated = await _repo.ReadPackageVersionsAsync(propsPath);
        updated.Should().NotContainKey("Newtonsoft.Json");
    }

    [Fact]
    public async Task RemovePackageVersionAsync_NonexistentPackage_DoesNotThrow()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("OtherPkg", "1.0.0")));

        Func<Task> act = () => _repo.RemovePackageVersionAsync(propsPath, "MissingPkg");

        await act.Should().NotThrowAsync();
        var updated = await _repo.ReadPackageVersionsAsync(propsPath);
        updated.Should().ContainKey("OtherPkg"); // unrelated package preserved
    }

    public void Dispose() => _temp.Dispose();
}
