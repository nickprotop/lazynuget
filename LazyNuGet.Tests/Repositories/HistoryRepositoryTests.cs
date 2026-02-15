using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Repositories;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Repositories;

public class HistoryRepositoryTests : IDisposable
{
    private readonly TempDirectoryFixture _temp = new();

    private HistoryRepository CreateRepository(string fileName = "history.json")
    {
        return new HistoryRepository(Path.Combine(_temp.Path, fileName));
    }

    [Fact]
    public async Task LoadHistoryAsync_NoFile_ReturnsEmpty()
    {
        var repo = CreateRepository();
        var history = await repo.LoadHistoryAsync();
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesEntries()
    {
        var repo = CreateRepository();
        var entries = new List<OperationHistoryEntry>
        {
            SampleDataBuilder.CreateHistoryEntry(OperationType.Update, "ProjectA"),
            SampleDataBuilder.CreateHistoryEntry(OperationType.Add, "ProjectB", success: false, errorMessage: "Not found")
        };

        await repo.SaveHistoryAsync(entries);
        var loaded = await repo.LoadHistoryAsync();

        loaded.Should().HaveCount(2);
        loaded[0].ProjectName.Should().Be("ProjectA");
        loaded[1].ProjectName.Should().Be("ProjectB");
        loaded[1].Success.Should().BeFalse();
        loaded[1].ErrorMessage.Should().Be("Not found");
    }

    [Fact]
    public async Task SaveAndLoad_PreservesAllFields()
    {
        var repo = CreateRepository();
        var entry = SampleDataBuilder.CreateHistoryEntry();
        await repo.SaveHistoryAsync(new List<OperationHistoryEntry> { entry });

        var loaded = await repo.LoadHistoryAsync();
        var restored = loaded[0];

        restored.Id.Should().Be(entry.Id);
        restored.Type.Should().Be(entry.Type);
        restored.ProjectName.Should().Be(entry.ProjectName);
        restored.Description.Should().Be(entry.Description);
        restored.Success.Should().Be(entry.Success);
        restored.ProjectPath.Should().Be(entry.ProjectPath);
        restored.PackageId.Should().Be(entry.PackageId);
        restored.PackageVersion.Should().Be(entry.PackageVersion);
    }

    [Fact]
    public async Task LoadHistoryAsync_CorruptJson_ReturnsEmpty()
    {
        _temp.WriteFile("corrupt.json", "{{{{not valid json}}}}");
        var repo = CreateRepository("corrupt.json");

        var history = await repo.LoadHistoryAsync();
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadHistoryAsync_EmptyFile_ReturnsEmpty()
    {
        _temp.WriteFile("empty.json", "");
        var repo = CreateRepository("empty.json");

        var history = await repo.LoadHistoryAsync();
        history.Should().BeEmpty();
    }

    [Fact]
    public void HistoryFileExists_NoFile_ReturnsFalse()
    {
        var repo = CreateRepository();
        repo.HistoryFileExists().Should().BeFalse();
    }

    [Fact]
    public async Task HistoryFileExists_AfterSave_ReturnsTrue()
    {
        var repo = CreateRepository();
        await repo.SaveHistoryAsync(new List<OperationHistoryEntry>
        {
            SampleDataBuilder.CreateHistoryEntry()
        });

        repo.HistoryFileExists().Should().BeTrue();
    }

    [Fact]
    public async Task DeleteHistoryFile_RemovesFile()
    {
        var repo = CreateRepository();
        await repo.SaveHistoryAsync(new List<OperationHistoryEntry>
        {
            SampleDataBuilder.CreateHistoryEntry()
        });

        repo.DeleteHistoryFile();
        repo.HistoryFileExists().Should().BeFalse();
    }

    [Fact]
    public void DeleteHistoryFile_NoFile_DoesNotThrow()
    {
        var repo = CreateRepository();
        Action act = () => repo.DeleteHistoryFile();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task SaveHistoryAsync_EmptyList_CreatesFile()
    {
        var repo = CreateRepository();
        await repo.SaveHistoryAsync(new List<OperationHistoryEntry>());

        repo.HistoryFileExists().Should().BeTrue();
    }

    [Fact]
    public async Task SaveHistoryAsync_OverwritesPreviousData()
    {
        var repo = CreateRepository();

        await repo.SaveHistoryAsync(new List<OperationHistoryEntry>
        {
            SampleDataBuilder.CreateHistoryEntry(projectName: "First")
        });

        await repo.SaveHistoryAsync(new List<OperationHistoryEntry>
        {
            SampleDataBuilder.CreateHistoryEntry(projectName: "Second")
        });

        var loaded = await repo.LoadHistoryAsync();
        loaded.Should().HaveCount(1);
        loaded[0].ProjectName.Should().Be("Second");
    }

    [Fact]
    public void Constructor_NullPath_ThrowsArgumentNull()
    {
        Action act = () => new HistoryRepository(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    public void Dispose() => _temp.Dispose();
}
