using System.Xml.Linq;
using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Repositories;
using LazyNuGet.Services;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Services;

public class CpmMigrationServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _temp = new();
    private readonly CpmMigrationService _sut = new();

    // ── AnalyzeAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task MigrateToCpm_SimpleProject_CreatesPropsFile()
    {
        // Arrange
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0",
            ("Newtonsoft.Json", "13.0.1"),
            ("Serilog", "3.1.1"));
        _temp.WriteFile("MyApp.csproj", csproj);

        // Act: analyze then migrate
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        var result   = await _sut.MigrateAsync(_temp.Path, analysis);

        // Assert
        result.Success.Should().BeTrue();
        var propsPath = Path.Combine(_temp.Path, "Directory.Packages.props");
        File.Exists(propsPath).Should().BeTrue("Directory.Packages.props should be created");

        var doc = XDocument.Load(propsPath);
        var entries = doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageVersion")
            .ToList();
        entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task MigrateToCpm_MultipleProjects_CentralizesAllPackages()
    {
        // Arrange: two projects with distinct packages
        _temp.WriteFile("Sub1/Proj1.csproj",
            SampleDataBuilder.CreateValidCsproj("net9.0", ("Newtonsoft.Json", "13.0.1")));
        _temp.WriteFile("Sub2/Proj2.csproj",
            SampleDataBuilder.CreateValidCsproj("net9.0", ("Serilog", "3.1.1")));

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        var result   = await _sut.MigrateAsync(_temp.Path, analysis);

        // Assert
        result.Success.Should().BeTrue();
        result.ProjectsMigrated.Should().Be(2);
        result.PackagesCentralized.Should().Be(2);

        var propsPath = Path.Combine(_temp.Path, "Directory.Packages.props");
        var doc = XDocument.Load(propsPath);
        var ids = doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageVersion")
            .Select(e => e.Attribute("Include")?.Value)
            .ToList();
        ids.Should().Contain("Newtonsoft.Json");
        ids.Should().Contain("Serilog");
    }

    [Fact]
    public async Task MigrateToCpm_VersionConflict_PicksHighestVersion()
    {
        // Arrange: same package with different versions in two projects
        _temp.WriteFile("A/A.csproj",
            SampleDataBuilder.CreateValidCsproj("net9.0", ("Newtonsoft.Json", "12.0.3")));
        _temp.WriteFile("B/B.csproj",
            SampleDataBuilder.CreateValidCsproj("net9.0", ("Newtonsoft.Json", "13.0.1")));

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);

        // Assert: conflict detected
        analysis.VersionConflictsCount.Should().Be(1);
        analysis.ResolvedVersions["Newtonsoft.Json"].Should().Be("13.0.1",
            "highest version should be selected");

        var result = await _sut.MigrateAsync(_temp.Path, analysis);
        result.VersionConflictsResolved.Should().Be(1);

        var propsPath = Path.Combine(_temp.Path, "Directory.Packages.props");
        var doc = XDocument.Load(propsPath);
        var entry = doc.Descendants()
            .First(e => e.Name.LocalName == "PackageVersion"
                     && e.Attribute("Include")?.Value == "Newtonsoft.Json");
        entry.Attribute("Version")?.Value.Should().Be("13.0.1");
    }

    [Fact]
    public async Task MigrateToCpm_RemovesVersionAttributeFromPackageReference()
    {
        // Arrange
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0", ("Newtonsoft.Json", "13.0.1"));
        var projectPath = _temp.WriteFile("MyApp.csproj", csproj);

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        await _sut.MigrateAsync(_temp.Path, analysis);

        // Assert: Version attribute removed
        var doc     = XDocument.Load(projectPath);
        var pkgRefs = doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .ToList();
        pkgRefs.Should().HaveCount(1);
        pkgRefs[0].Attribute("Version").Should().BeNull(
            "Version attribute should be removed after CPM migration");
        pkgRefs[0].Attribute("Include")?.Value.Should().Be("Newtonsoft.Json");
    }

    [Fact]
    public async Task MigrateToCpm_DoesNotRemoveVersionOverrideAttribute()
    {
        // Arrange: project with one normal ref and one VersionOverride ref
        const string csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
                <PackageReference Include="Serilog" VersionOverride="3.1.1" />
              </ItemGroup>
            </Project>
            """;
        var projectPath = _temp.WriteFile("MyApp.csproj", csproj);

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);

        // Assert: only the normal ref is in inlineRefs (VersionOverride is skipped)
        analysis.ProjectsToMigrate.Should().HaveCount(1);
        analysis.ProjectsToMigrate[0].InlineRefs.Should().HaveCount(1);
        analysis.ProjectsToMigrate[0].InlineRefs[0].Id.Should().Be("Newtonsoft.Json");

        await _sut.MigrateAsync(_temp.Path, analysis);

        var doc    = XDocument.Load(projectPath);
        var serilog = doc.Descendants()
            .First(e => e.Name.LocalName == "PackageReference"
                     && e.Attribute("Include")?.Value == "Serilog");
        serilog.Attribute("VersionOverride")?.Value.Should().Be("3.1.1",
            "VersionOverride should be preserved untouched");
    }

    [Fact]
    public async Task MigrateToCpm_SkipsUpdatePackageReferences()
    {
        // Arrange: project with an Update ref (SDK-implicit override)
        const string csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
                <PackageReference Update="System.Text.Json" Version="9.0.0" />
              </ItemGroup>
            </Project>
            """;
        var projectPath = _temp.WriteFile("MyApp.csproj", csproj);

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);

        // Assert: Update ref should NOT be in inlineRefs
        analysis.ProjectsToMigrate.Should().HaveCount(1);
        var refs = analysis.ProjectsToMigrate[0].InlineRefs;
        refs.Should().ContainSingle(r => r.Id == "Newtonsoft.Json");
        refs.Should().NotContain(r => r.Id == "System.Text.Json",
            "PackageReference Update elements should be skipped");

        await _sut.MigrateAsync(_temp.Path, analysis);

        var doc    = XDocument.Load(projectPath);
        var update = doc.Descendants()
            .First(e => e.Name.LocalName == "PackageReference"
                     && e.Attribute("Update")?.Value == "System.Text.Json");
        update.Attribute("Version")?.Value.Should().Be("9.0.0",
            "Update PackageReference Version should be left untouched");
    }

    [Fact]
    public async Task MigrateToCpm_CreatesBackupBeforeChanges()
    {
        // Arrange
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0", ("Newtonsoft.Json", "13.0.1"));
        var projectPath = _temp.WriteFile("MyApp.csproj", csproj);

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        var result   = await _sut.MigrateAsync(_temp.Path, analysis);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(projectPath + ".bak").Should().BeTrue("backup file should be created before migration");
        (await File.ReadAllTextAsync(projectPath + ".bak")).Should().Be(csproj,
            "backup should contain original content");
    }

    [Fact]
    public async Task MigrateToCpm_AlreadyCpmProject_IsSkipped()
    {
        // Arrange: project with no inline versions (already CPM)
        var csproj = SampleDataBuilder.CreateCpmCsproj("net9.0", "Newtonsoft.Json", "Serilog");
        _temp.WriteFile("MyApp.csproj", csproj);

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);

        // Assert: project is in skipped list, nothing to migrate
        analysis.ProjectsToMigrate.Should().BeEmpty();
        analysis.ProjectsSkipped.Should().HaveCount(1);
        analysis.ProjectsSkipped[0].SkipReason.Should().Contain("CPM");
    }

    [Fact]
    public async Task MigrateToCpm_PackagesConfigProject_IsSkipped()
    {
        // Arrange: project that uses packages.config
        _temp.WriteFile("LegacyApp.csproj",
            SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1")));
        _temp.WriteFile("packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);

        // Assert
        analysis.ProjectsToMigrate.Should().BeEmpty();
        analysis.ProjectsSkipped.Should().HaveCount(1);
        analysis.ProjectsSkipped[0].SkipReason.Should().Contain("packages.config");
    }

    [Fact]
    public async Task MigrateToCpm_EmptyFolder_ReturnsZeroMigrated()
    {
        // Act: empty temp dir with no projects
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        var result   = await _sut.MigrateAsync(_temp.Path, analysis);

        // Assert
        analysis.ProjectsToMigrate.Should().BeEmpty();
        result.ProjectsMigrated.Should().Be(0);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task MigrateToCpm_ExistingPropsFile_MergesNewEntries()
    {
        // Arrange: existing props file with one package; project adds a new package
        _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("Serilog", "3.0.0")));
        _temp.WriteFile("MyApp.csproj",
            SampleDataBuilder.CreateValidCsproj("net9.0", ("Newtonsoft.Json", "13.0.1")));

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        var result   = await _sut.MigrateAsync(_temp.Path, analysis);

        // Assert
        result.Success.Should().BeTrue();
        var doc = XDocument.Load(Path.Combine(_temp.Path, "Directory.Packages.props"));
        var entries = doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageVersion")
            .ToDictionary(
                e => e.Attribute("Include")?.Value ?? "",
                e => e.Attribute("Version")?.Value ?? "",
                StringComparer.OrdinalIgnoreCase);

        entries.Should().ContainKey("Serilog");
        entries.Should().ContainKey("Newtonsoft.Json");
        entries["Serilog"].Should().Be("3.0.0", "existing entry should be preserved");
    }

    [Fact]
    public async Task MigrateToCpm_ExistingPropsFile_NeverDowngradesVersion()
    {
        // Arrange: props file already has Newtonsoft.Json 13.0.1; project only has 12.0.3
        _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("Newtonsoft.Json", "13.0.1")));
        _temp.WriteFile("MyApp.csproj",
            SampleDataBuilder.CreateValidCsproj("net9.0", ("Newtonsoft.Json", "12.0.3")));

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        var result   = await _sut.MigrateAsync(_temp.Path, analysis);

        // Assert: version should remain 13.0.1, NOT be downgraded to 12.0.3
        result.Success.Should().BeTrue();
        var doc = XDocument.Load(Path.Combine(_temp.Path, "Directory.Packages.props"));
        var entry = doc.Descendants()
            .First(e => e.Name.LocalName == "PackageVersion"
                     && string.Equals(e.Attribute("Include")?.Value, "Newtonsoft.Json",
                         StringComparison.OrdinalIgnoreCase));
        entry.Attribute("Version")?.Value.Should().Be("13.0.1",
            "existing higher version in props file should never be downgraded");
    }

    [Fact]
    public async Task MigrateToCpm_MixedProject_OnlyInlineVersionsRemoved()
    {
        // Arrange: project that already has one CPM ref (no Version) and one inline ref
        const string csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
                <PackageReference Include="Serilog" />
              </ItemGroup>
            </Project>
            """;
        var projectPath = _temp.WriteFile("MyApp.csproj", csproj);

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        await _sut.MigrateAsync(_temp.Path, analysis);

        // Assert: only the inline Version was removed; Serilog ref stays without Version
        var doc = XDocument.Load(projectPath);
        var refs = doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .ToList();
        refs.Should().HaveCount(2);

        var njRef = refs.First(r => r.Attribute("Include")?.Value == "Newtonsoft.Json");
        njRef.Attribute("Version").Should().BeNull("inline Version should have been removed");

        var serilogRef = refs.First(r => r.Attribute("Include")?.Value == "Serilog");
        serilogRef.Attribute("Version").Should().BeNull("Serilog never had a Version attr");
    }

    [Fact]
    public async Task MigrateToCpm_CancellationToken_StopsEarly()
    {
        // Arrange: project to migrate
        _temp.WriteFile("MyApp.csproj",
            SampleDataBuilder.CreateValidCsproj("net9.0", ("Newtonsoft.Json", "13.0.1")));

        var analysis = await _sut.AnalyzeAsync(_temp.Path);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Already cancelled before MigrateAsync is called

        // Act
        var result = await _sut.MigrateAsync(_temp.Path, analysis, ct: cts.Token);

        // Assert: migration should report failure with cancellation message
        result.Success.Should().BeFalse();
        result.Error!.ToLowerInvariant().Should().Contain("cancel");

        // No Directory.Packages.props should have been created (failed before writing)
        File.Exists(Path.Combine(_temp.Path, "Directory.Packages.props")).Should().BeFalse();
    }

    [Fact]
    public async Task MigrateToCpm_RestoredFromBackupOnFailure()
    {
        // Arrange: two projects so backups are created
        var csproj1 = SampleDataBuilder.CreateValidCsproj("net9.0", ("Newtonsoft.Json", "13.0.1"));
        var csproj2 = SampleDataBuilder.CreateValidCsproj("net9.0", ("Serilog", "3.1.1"));
        var path1 = _temp.WriteFile("Sub1/Proj1.csproj", csproj1);
        var path2 = _temp.WriteFile("Sub2/Proj2.csproj", csproj2);

        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        analysis.ProjectsToMigrate.Should().HaveCount(2);

        // Force failure: create a directory at the props path so File.Save throws
        Directory.CreateDirectory(Path.Combine(_temp.Path, "Directory.Packages.props"));

        // Act: migration should fail and rollback
        var result = await _sut.MigrateAsync(_temp.Path, analysis);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();

        // Backup files should exist (were created before the failure)
        File.Exists(path1 + ".bak").Should().BeTrue("backup for Proj1 should exist");
        File.Exists(path2 + ".bak").Should().BeTrue("backup for Proj2 should exist");

        // Project files should be restored (rollback copied .bak back)
        (await File.ReadAllTextAsync(path1)).Should().Be(csproj1,
            "Proj1 should be restored from backup");
        (await File.ReadAllTextAsync(path2)).Should().Be(csproj2,
            "Proj2 should be restored from backup");
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MigrateToCpm_VersionChildElement_IsDetectedAndRemoved()
    {
        // Arrange: version expressed as a child element, not an attribute
        const string csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json">
                  <Version>13.0.1</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;
        var projectPath = _temp.WriteFile("MyApp.csproj", csproj);

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);

        // Detected as inline ref
        analysis.ProjectsToMigrate.Should().HaveCount(1);
        analysis.ProjectsToMigrate[0].InlineRefs.Should().ContainSingle(r =>
            r.Id == "Newtonsoft.Json" && r.Version == "13.0.1");

        await _sut.MigrateAsync(_temp.Path, analysis);

        // <Version> child element removed
        var doc = XDocument.Load(projectPath);
        var pkgRef = doc.Descendants()
            .First(e => e.Name.LocalName == "PackageReference");
        pkgRef.Elements().Where(e => e.Name.LocalName == "Version").Should().BeEmpty(
            "<Version> child element should be removed after CPM migration");

        // Props file contains the version
        var propsDoc = XDocument.Load(Path.Combine(_temp.Path, "Directory.Packages.props"));
        propsDoc.Descendants()
            .First(e => e.Name.LocalName == "PackageVersion"
                     && e.Attribute("Include")?.Value == "Newtonsoft.Json")
            .Attribute("Version")?.Value.Should().Be("13.0.1");
    }

    [Fact]
    public async Task MigrateToCpm_VersionRange_KeptVerbatim()
    {
        // Arrange: version is a NuGet range expression — TryParse fails, must be kept verbatim
        const string csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.Extensions.Http" Version="[7.0.0, 8.0.0)" />
                <PackageReference Include="Serilog" Version="3.1.1" />
              </ItemGroup>
            </Project>
            """;
        _temp.WriteFile("MyApp.csproj", csproj);

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        var result   = await _sut.MigrateAsync(_temp.Path, analysis);

        // Assert: version range is kept verbatim in the props file
        result.Success.Should().BeTrue();
        var doc = XDocument.Load(Path.Combine(_temp.Path, "Directory.Packages.props"));
        var entries = doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageVersion")
            .ToDictionary(
                e => e.Attribute("Include")?.Value ?? "",
                e => e.Attribute("Version")?.Value ?? "");

        entries["Microsoft.Extensions.Http"].Should().Be("[7.0.0, 8.0.0)",
            "version range should be kept verbatim");
        entries["Serilog"].Should().Be("3.1.1");
    }

    [Fact]
    public async Task MigrateToCpm_MultiTargetFramework_MigratedSuccessfully()
    {
        // Arrange: project targeting multiple frameworks
        const string csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net9.0;net8.0;net7.0</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
                <PackageReference Include="Serilog" Version="3.1.1" />
              </ItemGroup>
            </Project>
            """;
        var projectPath = _temp.WriteFile("MultiTarget.csproj", csproj);

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        var result   = await _sut.MigrateAsync(_temp.Path, analysis);

        // Assert: migrated correctly regardless of multi-targeting
        result.Success.Should().BeTrue();
        result.ProjectsMigrated.Should().Be(1);
        result.PackagesCentralized.Should().Be(2);

        var doc = XDocument.Load(projectPath);
        doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .All(r => r.Attribute("Version") == null).Should().BeTrue(
                "all inline Version attributes should be removed");

        // TargetFrameworks element must be preserved
        doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "TargetFrameworks")?
            .Value.Should().Be("net9.0;net8.0;net7.0");
    }

    [Fact]
    public async Task MigrateToCpm_VersionConflict_LexicographicFallback()
    {
        // Arrange: both versions are ranges (TryParse fails) — lexicographic max is used
        _temp.WriteFile("A/A.csproj",
            SampleDataBuilder.CreateValidCsproj("net9.0", ("SomeLib", "[1.0.0, 2.0.0)")));
        _temp.WriteFile("B/B.csproj",
            SampleDataBuilder.CreateValidCsproj("net9.0", ("SomeLib", "[1.5.0, 2.0.0)")));

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);

        // Both fail NuGetVersion.TryParse → lexicographic max
        analysis.VersionConflictsCount.Should().Be(1);
        // "[1.5.0, 2.0.0)" > "[1.0.0, 2.0.0)" lexicographically (5 > 0 at position 2)
        analysis.ResolvedVersions["SomeLib"].Should().Be("[1.5.0, 2.0.0)");
    }

    // ── Round-trip: ProjectRepository reads correctly after CPM migration ─────

    [Fact]
    public async Task MigrateToCpm_RoundTrip_ProjectRepositoryDetectsCpmEnabled()
    {
        // Arrange
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0",
            ("Newtonsoft.Json", "13.0.1"),
            ("Serilog", "3.1.1"));
        var projectPath = _temp.WriteFile("MyApp.csproj", csproj);

        // Act: run CPM migration
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        var result   = await _sut.MigrateAsync(_temp.Path, analysis);
        result.Success.Should().BeTrue();

        // Re-read with ProjectRepository
        var repo    = new ProjectRepository();
        var data    = await repo.ReadProjectFileAsync(projectPath);

        // Assert CPM is detected
        data.Should().NotBeNull();
        data!.IsCpmEnabled.Should().BeTrue(
            "ProjectRepository should detect CPM from Directory.Packages.props after migration");
        data.PropsFilePath.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task MigrateToCpm_RoundTrip_PackagesHaveCentralVersionSource()
    {
        // Arrange
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0",
            ("Newtonsoft.Json", "13.0.1"),
            ("Serilog", "3.1.1"));
        var projectPath = _temp.WriteFile("MyApp.csproj", csproj);

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        await _sut.MigrateAsync(_temp.Path, analysis);

        var repo = new ProjectRepository();
        var data = await repo.ReadProjectFileAsync(projectPath);

        // Assert: packages read back with Central version source
        data.Should().NotBeNull();
        data!.PackageReferences.Should().HaveCount(2);
        data.PackageReferences.Should().AllSatisfy(p =>
            p.VersionSource.Should().Be(VersionSource.Central,
                "migrated packages should have VersionSource.Central"));
    }

    [Fact]
    public async Task MigrateToCpm_RoundTrip_PackageVersionsAreCorrect()
    {
        // Arrange
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0",
            ("Newtonsoft.Json", "13.0.1"),
            ("Serilog", "3.1.1"));
        var projectPath = _temp.WriteFile("MyApp.csproj", csproj);

        // Act
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        await _sut.MigrateAsync(_temp.Path, analysis);

        var repo = new ProjectRepository();
        var data = await repo.ReadProjectFileAsync(projectPath);

        // Assert: versions are read correctly from the props file
        data.Should().NotBeNull();
        var nj      = data!.PackageReferences.FirstOrDefault(p => p.Id == "Newtonsoft.Json");
        var serilog = data.PackageReferences.FirstOrDefault(p => p.Id == "Serilog");

        nj.Should().NotBeNull();
        nj!.Version.Should().Be("13.0.1");

        serilog.Should().NotBeNull();
        serilog!.Version.Should().Be("3.1.1");
    }

    [Fact]
    public async Task MigrateToCpm_RoundTrip_VersionConflict_HighestVersionInRepo()
    {
        // Arrange: two projects disagree on Newtonsoft.Json version
        _temp.WriteFile("ProjectA/A.csproj",
            SampleDataBuilder.CreateValidCsproj("net9.0", ("Newtonsoft.Json", "12.0.3")));
        _temp.WriteFile("ProjectB/B.csproj",
            SampleDataBuilder.CreateValidCsproj("net9.0", ("Newtonsoft.Json", "13.0.1")));

        // Act: migrate and re-read ProjectA
        var analysis = await _sut.AnalyzeAsync(_temp.Path);
        await _sut.MigrateAsync(_temp.Path, analysis);

        var repo = new ProjectRepository();
        var dataA = await repo.ReadProjectFileAsync(
            Path.Combine(_temp.Path, "ProjectA", "A.csproj"));

        // Assert: ProjectA now resolves to 13.0.1 (the highest version, from props)
        dataA.Should().NotBeNull();
        var nj = dataA!.PackageReferences.First(p => p.Id == "Newtonsoft.Json");
        nj.Version.Should().Be("13.0.1",
            "the conflict should have been resolved to the highest version");
        nj.VersionSource.Should().Be(VersionSource.Central);
    }

    public void Dispose() => _temp.Dispose();
}
