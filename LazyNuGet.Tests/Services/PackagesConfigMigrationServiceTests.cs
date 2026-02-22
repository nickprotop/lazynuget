using System.Xml.Linq;
using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Repositories;
using LazyNuGet.Services;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Services;

public class PackagesConfigMigrationServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _temp = new();
    private readonly PackagesConfigMigrationService _sut = new();

    // ── Happy-path migration ──────────────────────────────────────────────────

    [Fact]
    public async Task MigrateProject_SimplePackages_ConvertsToPackageReference()
    {
        // Arrange
        var csproj = SampleDataBuilder.CreateLegacyCsproj(
            ("Newtonsoft.Json", "13.0.1"),
            ("Serilog", "3.1.1"));
        var config = SampleDataBuilder.CreatePackagesConfig(
            ("Newtonsoft.Json", "13.0.1", "net45"),
            ("Serilog", "3.1.1", "net45"));

        var projectPath = _temp.WriteFile("MyApp.csproj", csproj);
        _temp.WriteFile("packages.config", config);

        // Act
        var result = await _sut.MigrateProjectAsync(projectPath);

        // Assert
        result.Success.Should().BeTrue();
        result.PackagesMigrated.Should().Be(2);
        result.Error.Should().BeNull();

        var doc = XDocument.Load(projectPath);
        var pkgRefs = doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .ToList();
        pkgRefs.Should().HaveCount(2);

        var hasNewtonsoft = pkgRefs.Any(r =>
            r.Attribute("Include")?.Value == "Newtonsoft.Json" &&
            r.Attribute("Version")?.Value == "13.0.1");
        hasNewtonsoft.Should().BeTrue("Newtonsoft.Json 13.0.1 PackageReference should exist");

        var hasSerilog = pkgRefs.Any(r =>
            r.Attribute("Include")?.Value == "Serilog" &&
            r.Attribute("Version")?.Value == "3.1.1");
        hasSerilog.Should().BeTrue("Serilog 3.1.1 PackageReference should exist");
    }

    [Fact]
    public async Task MigrateProject_RemovesHintPathReferences_PreservesFrameworkRefs()
    {
        // Arrange
        var csproj = SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1"));
        var config = SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45"));

        var projectPath = _temp.WriteFile("MyApp.csproj", csproj);
        _temp.WriteFile("packages.config", config);

        // Act
        var result = await _sut.MigrateProjectAsync(projectPath);

        // Assert
        result.Success.Should().BeTrue();

        var doc = XDocument.Load(projectPath);

        // NuGet HintPath reference should be removed
        var hintPathRefs = doc.Descendants()
            .Where(e => e.Name.LocalName == "Reference")
            .Where(r => r.Elements().Any(e => e.Name.LocalName == "HintPath"
                        && e.Value.IndexOf("packages", StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();
        hintPathRefs.Should().BeEmpty("NuGet HintPath references should be removed");

        // Framework references (System, System.Core, System.Xml) must be preserved
        var frameworkRefs = doc.Descendants()
            .Where(e => e.Name.LocalName == "Reference")
            .Select(r => r.Attribute("Include")?.Value)
            .Where(v => v != null)
            .ToList();
        frameworkRefs.Should().Contain("System");
        frameworkRefs.Should().Contain("System.Core");
        frameworkRefs.Should().Contain("System.Xml");
    }

    [Fact]
    public async Task MigrateProject_DeletesPackagesConfig_AfterSuccess()
    {
        // Arrange
        var projectPath = _temp.WriteFile("MyApp.csproj",
            SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1")));
        var configPath = _temp.WriteFile("packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));

        // Act
        var result = await _sut.MigrateProjectAsync(projectPath);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(configPath).Should().BeFalse("packages.config should be deleted after migration");
    }

    [Fact]
    public async Task MigrateProject_CreatesBackupBeforeChanges()
    {
        // Arrange
        var projectPath = _temp.WriteFile("MyApp.csproj",
            SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1")));
        _temp.WriteFile("packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));

        // Act
        var result = await _sut.MigrateProjectAsync(projectPath);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(projectPath + ".bak").Should().BeTrue("backup file should be created");
    }

    [Fact]
    public async Task MigrateProject_BackupContainsOriginalContent()
    {
        // Arrange
        var originalContent = SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1"));
        var projectPath = _temp.WriteFile("MyApp.csproj", originalContent);
        _temp.WriteFile("packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));

        // Act
        await _sut.MigrateProjectAsync(projectPath);

        // Assert
        var backupContent = await File.ReadAllTextAsync(projectPath + ".bak");
        backupContent.Should().Be(originalContent);
    }

    [Fact]
    public async Task MigrateProject_RemovesNuGetImportElements()
    {
        // Arrange
        var projectPath = _temp.WriteFile("MyApp.csproj",
            SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1")));
        _temp.WriteFile("packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));

        // Act
        var result = await _sut.MigrateProjectAsync(projectPath);

        // Assert
        result.Success.Should().BeTrue();
        var doc = XDocument.Load(projectPath);
        var nugetImports = doc.Descendants()
            .Where(e => e.Name.LocalName == "Import")
            .Where(i =>
            {
                var proj = i.Attribute("Project")?.Value ?? "";
                return proj.Contains("packages", StringComparison.OrdinalIgnoreCase)
                       && proj.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        nugetImports.Should().BeEmpty("NuGet import targets should be removed");
    }

    // ── Error / edge cases ────────────────────────────────────────────────────

    [Fact]
    public async Task MigrateProject_AlreadyPackageReference_ReturnsError()
    {
        // Arrange: project already uses PackageReference
        var csproj = SampleDataBuilder.CreateValidCsproj("net9.0",
            ("Newtonsoft.Json", "13.0.1"));
        var projectPath = _temp.WriteFile("Modern.csproj", csproj);
        // Place a packages.config alongside it to trigger the check
        _temp.WriteFile("packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));

        // Act
        var result = await _sut.MigrateProjectAsync(projectPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("PackageReference");
    }

    [Fact]
    public async Task MigrateProject_MissingPackagesConfig_ReturnsError()
    {
        // Arrange: project file exists but no packages.config
        var projectPath = _temp.WriteFile("NoConfig.csproj",
            SampleDataBuilder.CreateLegacyCsproj());

        // Act
        var result = await _sut.MigrateProjectAsync(projectPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("packages.config");
    }

    [Fact]
    public async Task MigrateProject_EmptyPackagesConfig_Succeeds()
    {
        // Arrange: packages.config with zero packages
        var projectPath = _temp.WriteFile("EmptyPkgs.csproj",
            SampleDataBuilder.CreateLegacyCsproj());
        _temp.WriteFile("packages.config",
            SampleDataBuilder.CreatePackagesConfig());  // no packages

        // Act
        var result = await _sut.MigrateProjectAsync(projectPath);

        // Assert
        result.Success.Should().BeTrue();
        result.PackagesMigrated.Should().Be(0);
        // packages.config should be deleted even when empty
        File.Exists(_temp.Path + "/packages.config").Should().BeFalse();
    }

    [Fact]
    public async Task MigrateProject_NonexistentProjectFile_ReturnsError()
    {
        var result = await _sut.MigrateProjectAsync("/nonexistent/path/Project.csproj");
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task MigrateProject_MalformedPackagesConfig_ReturnsEmptyMigration()
    {
        // Arrange: malformed packages.config — ParsePackagesConfig returns empty list
        var projectPath = _temp.WriteFile("MalformedConfig.csproj",
            SampleDataBuilder.CreateLegacyCsproj());
        _temp.WriteFile("packages.config", "this is not valid xml <<<<");

        // Act — should fail because XDocument.Load throws
        var result = await _sut.MigrateProjectAsync(projectPath);

        // The migration service catches the exception and restores backup
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task MigrateProject_RestoredFromBackupOnFailure()
    {
        // Arrange: we need a situation where the migration partially fails.
        // Use a project file that's valid XML but the packages.config is malformed.
        var originalContent = SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1"));
        var projectPath = _temp.WriteFile("FailMid.csproj", originalContent);
        _temp.WriteFile("packages.config", "not xml <<<<");

        // Act
        var result = await _sut.MigrateProjectAsync(projectPath);

        // Assert: backup was created and project file is restored to original
        result.Success.Should().BeFalse();
        var backupPath = projectPath + ".bak";
        if (File.Exists(backupPath))
        {
            var projectContent = await File.ReadAllTextAsync(projectPath);
            projectContent.Should().Be(originalContent);
        }
    }

    // ── MigrateAllInFolderAsync ───────────────────────────────────────────────

    [Fact]
    public async Task MigrateAllInFolder_FindsAndMigratesMultipleProjects()
    {
        // Arrange: two subdirectories each with a legacy project
        _temp.WriteFile("ProjectA/ProjectA.csproj",
            SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1")));
        _temp.WriteFile("ProjectA/packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));

        _temp.WriteFile("ProjectB/ProjectB.csproj",
            SampleDataBuilder.CreateLegacyCsproj(("Serilog", "3.1.1")));
        _temp.WriteFile("ProjectB/packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Serilog", "3.1.1", "net45")));

        // Act
        var results = await _sut.MigrateAllInFolderAsync(_temp.Path);

        // Assert
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
        results.Sum(r => r.PackagesMigrated).Should().Be(2);
    }

    [Fact]
    public async Task MigrateAllInFolder_EmptyFolder_ReturnsEmptyList()
    {
        var results = await _sut.MigrateAllInFolderAsync(_temp.Path);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task MigrateAllInFolder_NoProjectFileInDir_ReturnsErrorResult()
    {
        // packages.config exists but no .csproj
        _temp.WriteFile("Orphan/packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));

        var results = await _sut.MigrateAllInFolderAsync(_temp.Path);

        results.Should().HaveCount(1);
        results[0].Success.Should().BeFalse();
        results[0].Error.Should().Contain("No .csproj");
    }

    [Fact]
    public async Task MigrateAllInFolder_ReportsProgressMessages()
    {
        _temp.WriteFile("App/App.csproj",
            SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1")));
        _temp.WriteFile("App/packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));

        var progressMessages = new List<string>();
        var progress = new Progress<string>(msg => progressMessages.Add(msg));

        await _sut.MigrateAllInFolderAsync(_temp.Path, progress);

        // Allow progress handlers to fire
        await Task.Delay(50);

        progressMessages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task MigrateAllInFolder_CancellationToken_StopsEarly()
    {
        // Create multiple projects so we can cancel mid-way
        for (int i = 0; i < 5; i++)
        {
            _temp.WriteFile($"Project{i}/Proj{i}.csproj",
                SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1")));
            _temp.WriteFile($"Project{i}/packages.config",
                SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel immediately

        Func<Task> act = () => _sut.MigrateAllInFolderAsync(_temp.Path, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── MigrationResult record ────────────────────────────────────────────────

    [Fact]
    public void MigrationResult_Properties_AreCorrect()
    {
        var result = new MigrationResult("/path/to/Project.csproj", true, 3, null);
        result.ProjectPath.Should().Be("/path/to/Project.csproj");
        result.Success.Should().BeTrue();
        result.PackagesMigrated.Should().Be(3);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void MigrationResult_ErrorResult_HasExpectedValues()
    {
        var result = new MigrationResult("/path/to/Project.csproj", false, 0, "Something went wrong");
        result.Success.Should().BeFalse();
        result.PackagesMigrated.Should().Be(0);
        result.Error.Should().Be("Something went wrong");
    }

    // ── Round-trip: ProjectRepository reads correctly after migration ─────────

    [Fact]
    public async Task MigrateProject_RoundTrip_ProjectRepositoryReadsPackagesCorrectly()
    {
        // Arrange: legacy MSBuild-namespaced project
        var projectPath = _temp.WriteFile("LegacyApp.csproj",
            SampleDataBuilder.CreateLegacyCsproj(
                ("Newtonsoft.Json", "13.0.1"),
                ("Serilog", "3.1.1")));
        _temp.WriteFile("packages.config",
            SampleDataBuilder.CreatePackagesConfig(
                ("Newtonsoft.Json", "13.0.1", "net45"),
                ("Serilog", "3.1.1", "net45")));

        // Act: migrate
        var migrationResult = await _sut.MigrateProjectAsync(projectPath);
        migrationResult.Success.Should().BeTrue();

        // Re-read with ProjectRepository — must handle the legacy MSBuild XML namespace
        var repo = new ProjectRepository();
        var data = await repo.ReadProjectFileAsync(projectPath);

        // Assert: packages are visible
        data.Should().NotBeNull();
        data!.PackageReferences.Should().HaveCount(2,
            "ProjectRepository must find PackageReferences in a legacy (xmlns) .csproj after migration");
    }

    [Fact]
    public async Task MigrateProject_RoundTrip_VersionSourceIsInline()
    {
        // Arrange
        var projectPath = _temp.WriteFile("LegacyApp.csproj",
            SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1")));
        _temp.WriteFile("packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));

        // Act
        await _sut.MigrateProjectAsync(projectPath);

        var repo = new ProjectRepository();
        var data = await repo.ReadProjectFileAsync(projectPath);

        // Assert: migrated packages should be inline (no props file involved)
        data.Should().NotBeNull();
        data!.PackageReferences.Should().HaveCount(1);
        data.PackageReferences[0].VersionSource.Should().Be(VersionSource.Inline,
            "packages.config migration produces inline versions in the .csproj");
        data.PackageReferences[0].Version.Should().Be("13.0.1");
        data.PackageReferences[0].Id.Should().Be("Newtonsoft.Json");
    }

    [Fact]
    public async Task MigrateProject_RoundTrip_IsPackagesConfigFalseAfterMigration()
    {
        // Arrange
        var projectPath = _temp.WriteFile("LegacyApp.csproj",
            SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1")));
        _temp.WriteFile("packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));

        // Act
        await _sut.MigrateProjectAsync(projectPath);

        var repo = new ProjectRepository();
        var data = await repo.ReadProjectFileAsync(projectPath);

        // Assert: no longer detected as packages.config project
        data.Should().NotBeNull();
        data!.IsPackagesConfig.Should().BeFalse(
            "packages.config is deleted by migration so the project should read as modern PackageReference");
    }

    [Fact]
    public async Task MigrateProject_RoundTrip_IsCpmEnabledFalseWithoutPropsFile()
    {
        // Arrange: standalone migration with no Directory.Packages.props nearby
        var projectPath = _temp.WriteFile("LegacyApp.csproj",
            SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1")));
        _temp.WriteFile("packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));

        // Act
        await _sut.MigrateProjectAsync(projectPath);

        var repo = new ProjectRepository();
        var data = await repo.ReadProjectFileAsync(projectPath);

        // Assert: no CPM without a props file
        data.Should().NotBeNull();
        data!.IsCpmEnabled.Should().BeFalse(
            "there is no Directory.Packages.props so CPM should not be detected");
    }

    [Fact]
    public async Task MigrateProject_RoundTrip_IsCpmEnabledTrueWhenPropsFileExists()
    {
        // Arrange: a Directory.Packages.props already exists in the same folder
        // (e.g., other projects have already been CPM-migrated)
        _temp.WriteFile("Directory.Packages.props",
            SampleDataBuilder.CreatePropsFile(("Newtonsoft.Json", "13.0.1")));
        var projectPath = _temp.WriteFile("LegacyApp.csproj",
            SampleDataBuilder.CreateLegacyCsproj(("Newtonsoft.Json", "13.0.1")));
        _temp.WriteFile("packages.config",
            SampleDataBuilder.CreatePackagesConfig(("Newtonsoft.Json", "13.0.1", "net45")));

        // Act: after packages.config migration the inline versions are in the .csproj,
        // but ProjectRepository should also detect the nearby props file
        await _sut.MigrateProjectAsync(projectPath);

        var repo = new ProjectRepository();
        var data = await repo.ReadProjectFileAsync(projectPath);

        // Assert: CPM detected via props file
        data.Should().NotBeNull();
        data!.IsCpmEnabled.Should().BeTrue(
            "Directory.Packages.props exists in the folder so CPM should be detected");
    }

    [Fact]
    public async Task MigrateProject_RoundTrip_CorrectPackageCount()
    {
        // Arrange: three packages
        var projectPath = _temp.WriteFile("LegacyApp.csproj",
            SampleDataBuilder.CreateLegacyCsproj(
                ("Newtonsoft.Json", "13.0.1"),
                ("Serilog", "3.1.1"),
                ("AutoMapper", "12.0.1")));
        _temp.WriteFile("packages.config",
            SampleDataBuilder.CreatePackagesConfig(
                ("Newtonsoft.Json", "13.0.1", "net45"),
                ("Serilog", "3.1.1", "net45"),
                ("AutoMapper", "12.0.1", "net45")));

        // Act
        var migrationResult = await _sut.MigrateProjectAsync(projectPath);
        migrationResult.PackagesMigrated.Should().Be(3);

        var repo = new ProjectRepository();
        var data = await repo.ReadProjectFileAsync(projectPath);

        // Assert: same 3 packages are visible after round-trip
        data.Should().NotBeNull();
        data!.PackageReferences.Should().HaveCount(3,
            "all migrated packages should be readable by ProjectRepository");
        data.PackageReferences.Should().Contain(p => p.Id == "Newtonsoft.Json" && p.Version == "13.0.1");
        data.PackageReferences.Should().Contain(p => p.Id == "Serilog" && p.Version == "3.1.1");
        data.PackageReferences.Should().Contain(p => p.Id == "AutoMapper" && p.Version == "12.0.1");
    }

    public void Dispose() => _temp.Dispose();
}
