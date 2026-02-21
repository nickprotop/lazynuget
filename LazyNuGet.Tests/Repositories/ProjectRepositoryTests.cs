using FluentAssertions;
using LazyNuGet.Repositories;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Repositories;

public class ProjectRepositoryTests : IDisposable
{
    private readonly TempDirectoryFixture _temp = new();
    private readonly ProjectRepository _repo = new();

    [Fact]
    public async Task ReadProjectFileAsync_ValidCsproj_ParsesCorrectly()
    {
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0",
            ("Newtonsoft.Json", "13.0.3"),
            ("Serilog", "3.1.1"));
        var filePath = _temp.WriteFile("TestProject.csproj", csproj);

        var result = await _repo.ReadProjectFileAsync(filePath);

        result.Should().NotBeNull();
        result!.Name.Should().Be("TestProject");
        result.TargetFramework.Should().Be("net9.0");
        result.PackageReferences.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReadProjectFileAsync_PackageReferenceFromAttribute()
    {
        var csproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""MyPkg"" Version=""1.2.3"" />
  </ItemGroup>
</Project>";
        var filePath = _temp.WriteFile("AttrProject.csproj", csproj);

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.PackageReferences.Should().HaveCount(1);
        result.PackageReferences[0].Id.Should().Be("MyPkg");
        result.PackageReferences[0].Version.Should().Be("1.2.3");
    }

    [Fact]
    public async Task ReadProjectFileAsync_PackageReferenceFromElement()
    {
        var csproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""MyPkg"">
      <Version>4.5.6</Version>
    </PackageReference>
  </ItemGroup>
</Project>";
        var filePath = _temp.WriteFile("ElemProject.csproj", csproj);

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.PackageReferences.Should().HaveCount(1);
        result.PackageReferences[0].Version.Should().Be("4.5.6");
    }

    [Fact]
    public async Task ReadProjectFileAsync_MultipleTargetFrameworks_UsesFirst()
    {
        var csproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFrameworks>net8.0;net9.0</TargetFrameworks></PropertyGroup>
</Project>";
        var filePath = _temp.WriteFile("MultiTfm.csproj", csproj);

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.TargetFramework.Should().Be("net8.0");
    }

    [Fact]
    public async Task ReadProjectFileAsync_NoTargetFramework_ReturnsUnknown()
    {
        var csproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup></PropertyGroup>
</Project>";
        var filePath = _temp.WriteFile("NoTfm.csproj", csproj);

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.TargetFramework.Should().Be("unknown");
    }

    [Fact]
    public async Task ReadProjectFileAsync_SkipsRefWithoutVersion()
    {
        var csproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""NoVersion"" />
    <PackageReference Include=""HasVersion"" Version=""1.0.0"" />
  </ItemGroup>
</Project>";
        var filePath = _temp.WriteFile("Partial.csproj", csproj);

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.PackageReferences.Should().HaveCount(1);
        result.PackageReferences[0].Id.Should().Be("HasVersion");
    }

    [Fact]
    public async Task ReadProjectFileAsync_SkipsRefWithoutInclude()
    {
        var csproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Version=""1.0.0"" />
  </ItemGroup>
</Project>";
        var filePath = _temp.WriteFile("NoInclude.csproj", csproj);

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.PackageReferences.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadProjectFileAsync_InvalidXml_ReturnsNull()
    {
        var filePath = _temp.WriteFile("Invalid.csproj", "not xml content <<<<");
        var result = await _repo.ReadProjectFileAsync(filePath);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadProjectFileAsync_NonexistentFile_ReturnsNull()
    {
        var result = await _repo.ReadProjectFileAsync("/nonexistent/path/project.csproj");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadProjectFileAsync_SetsLastModified()
    {
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0");
        var filePath = _temp.WriteFile("ModTime.csproj", csproj);

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.LastModified.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReadProjectFileAsync_EmptyPackageList()
    {
        var csproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
</Project>";
        var filePath = _temp.WriteFile("Empty.csproj", csproj);

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.PackageReferences.Should().BeEmpty();
    }

    // --- DiscoverProjectFilesAsync ---

    [Fact]
    public async Task DiscoverProjectFilesAsync_FindsCsproj()
    {
        _temp.WriteFile("App.csproj", SampleDataBuilder.CreateValidCsproj("net9.0"));

        var files = await _repo.DiscoverProjectFilesAsync(_temp.Path);
        files.Should().HaveCount(1);
        files[0].Should().EndWith(".csproj");
    }

    [Fact]
    public async Task DiscoverProjectFilesAsync_FindsFsproj()
    {
        _temp.WriteFile("App.fsproj", SampleDataBuilder.CreateValidCsproj("net9.0"));

        var files = await _repo.DiscoverProjectFilesAsync(_temp.Path);
        files.Should().HaveCount(1);
        files[0].Should().EndWith(".fsproj");
    }

    [Fact]
    public async Task DiscoverProjectFilesAsync_FindsVbproj()
    {
        _temp.WriteFile("App.vbproj", SampleDataBuilder.CreateValidCsproj("net9.0"));

        var files = await _repo.DiscoverProjectFilesAsync(_temp.Path);
        files.Should().HaveCount(1);
        files[0].Should().EndWith(".vbproj");
    }

    [Fact]
    public async Task DiscoverProjectFilesAsync_FindsNestedProjects()
    {
        _temp.WriteFile("src/App1/App1.csproj", SampleDataBuilder.CreateValidCsproj("net9.0"));
        _temp.WriteFile("src/App2/App2.csproj", SampleDataBuilder.CreateValidCsproj("net9.0"));

        var files = await _repo.DiscoverProjectFilesAsync(_temp.Path);
        files.Should().HaveCount(2);
    }

    [Fact]
    public async Task DiscoverProjectFilesAsync_EmptyDirectory_ReturnsEmpty()
    {
        var files = await _repo.DiscoverProjectFilesAsync(_temp.Path);
        files.Should().BeEmpty();
    }

    // --- CPM (Central Package Management) ---

    [Fact]
    public async Task ReadProjectFileAsync_CpmEnabled_IsCpmEnabledTrue()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("Newtonsoft.Json", "13.0.3")));
        var csproj = SampleDataBuilder.CreateCpmCsproj("net9.0", "Newtonsoft.Json");
        var filePath = _temp.WriteFile("CpmProject.csproj", csproj);

        var result = await _repo.ReadProjectFileAsync(filePath);

        result.Should().NotBeNull();
        result!.IsCpmEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ReadProjectFileAsync_CpmEnabled_PropsFilePathSet()
    {
        var propsPath = _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("Newtonsoft.Json", "13.0.3")));
        var filePath = _temp.WriteFile("CpmProject.csproj",
            SampleDataBuilder.CreateCpmCsproj("net9.0", "Newtonsoft.Json"));

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.PropsFilePath.Should().NotBeNull();
        result.PropsFilePath.Should().EndWith("Directory.Packages.props");
    }

    [Fact]
    public async Task ReadProjectFileAsync_CpmEnabled_PackageVersionResolvedFromProps()
    {
        _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("Newtonsoft.Json", "13.0.3")));
        var filePath = _temp.WriteFile("CpmProject.csproj",
            SampleDataBuilder.CreateCpmCsproj("net9.0", "Newtonsoft.Json"));

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.PackageReferences.Should().HaveCount(1);
        result.PackageReferences[0].Id.Should().Be("Newtonsoft.Json");
        result.PackageReferences[0].Version.Should().Be("13.0.3");
    }

    [Fact]
    public async Task ReadProjectFileAsync_CpmEnabled_VersionSourceIsCentral()
    {
        _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("Newtonsoft.Json", "13.0.3")));
        var filePath = _temp.WriteFile("CpmProject.csproj",
            SampleDataBuilder.CreateCpmCsproj("net9.0", "Newtonsoft.Json"));

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.PackageReferences[0].VersionSource.Should().Be(LazyNuGet.Models.VersionSource.Central);
    }

    [Fact]
    public async Task ReadProjectFileAsync_CpmWithVersionOverride_VersionSourceIsOverride()
    {
        _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("Newtonsoft.Json", "13.0.1")));

        var csproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" VersionOverride=""13.0.3"" />
  </ItemGroup>
</Project>";
        var filePath = _temp.WriteFile("OverrideProject.csproj", csproj);

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.PackageReferences[0].VersionSource.Should().Be(LazyNuGet.Models.VersionSource.Override);
        result.PackageReferences[0].Version.Should().Be("13.0.3");
    }

    [Fact]
    public async Task ReadProjectFileAsync_NoCpm_IsCpmEnabledFalse()
    {
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0", ("Newtonsoft.Json", "13.0.3"));
        var filePath = _temp.WriteFile("NormalProject.csproj", csproj);

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.IsCpmEnabled.Should().BeFalse();
        result.PropsFilePath.Should().BeNull();
    }

    [Fact]
    public async Task ReadProjectFileAsync_NoCpm_VersionSourceIsInline()
    {
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0", ("Newtonsoft.Json", "13.0.3"));
        var filePath = _temp.WriteFile("NormalProject.csproj", csproj);

        var result = await _repo.ReadProjectFileAsync(filePath);

        result!.PackageReferences[0].VersionSource.Should().Be(LazyNuGet.Models.VersionSource.Inline);
    }

    [Fact]
    public async Task ReadProjectFileAsync_CpmEnabled_PackageWithoutPropsEntry_SkippedOrEmpty()
    {
        // Package is referenced in csproj but not in props → no resolved version → should be skipped
        _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("SomethingElse", "1.0.0")));
        var filePath = _temp.WriteFile("CpmProject.csproj",
            SampleDataBuilder.CreateCpmCsproj("net9.0", "MissingFromProps"));

        var result = await _repo.ReadProjectFileAsync(filePath);

        // Package without a resolved version should not appear in the list
        result!.PackageReferences.Should().BeEmpty();
    }

    // --- Simple helpers ---

    [Fact]
    public void ProjectFileExists_ExistingFile_ReturnsTrue()
    {
        var filePath = _temp.WriteFile("Exists.csproj", SampleDataBuilder.CreateValidCsproj("net9.0"));
        _repo.ProjectFileExists(filePath).Should().BeTrue();
    }

    [Fact]
    public void ProjectFileExists_NonexistentFile_ReturnsFalse()
    {
        _repo.ProjectFileExists("/nonexistent/path.csproj").Should().BeFalse();
    }

    public void Dispose() => _temp.Dispose();
}
