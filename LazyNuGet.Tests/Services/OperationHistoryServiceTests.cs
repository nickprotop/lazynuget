using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Services;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Services;

public class OperationHistoryServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _temp = new();

    private OperationHistoryService CreateService()
    {
        return new OperationHistoryService(_temp.Path);
    }

    [Fact]
    public void AddEntry_InsertsMostRecentFirst()
    {
        var service = CreateService();
        service.AddEntry(SampleDataBuilder.CreateHistoryEntry(projectName: "First"));
        service.AddEntry(SampleDataBuilder.CreateHistoryEntry(projectName: "Second"));

        var history = service.GetHistory();
        history[0].ProjectName.Should().Be("Second");
        history[1].ProjectName.Should().Be("First");
    }

    [Fact]
    public void AddEntry_TrimsToMaxSize()
    {
        var service = CreateService();

        // Add 105 entries (max is 100)
        for (int i = 0; i < 105; i++)
        {
            service.AddEntry(SampleDataBuilder.CreateHistoryEntry(projectName: $"Project{i}"));
        }

        var history = service.GetHistory(200);
        history.Should().HaveCount(100);
    }

    [Fact]
    public void GetHistory_DefaultLimit_Returns50()
    {
        var service = CreateService();

        for (int i = 0; i < 60; i++)
        {
            service.AddEntry(SampleDataBuilder.CreateHistoryEntry(projectName: $"P{i}"));
        }

        var history = service.GetHistory();
        history.Should().HaveCount(50);
    }

    [Fact]
    public void GetHistory_CustomLimit()
    {
        var service = CreateService();

        for (int i = 0; i < 20; i++)
        {
            service.AddEntry(SampleDataBuilder.CreateHistoryEntry(projectName: $"P{i}"));
        }

        var history = service.GetHistory(5);
        history.Should().HaveCount(5);
    }

    [Fact]
    public void GetHistory_FewerThanLimit_ReturnsAll()
    {
        var service = CreateService();
        service.AddEntry(SampleDataBuilder.CreateHistoryEntry());

        var history = service.GetHistory(50);
        history.Should().HaveCount(1);
    }

    [Fact]
    public void GetFailedOperations_FiltersCorrectly()
    {
        var service = CreateService();
        service.AddEntry(SampleDataBuilder.CreateHistoryEntry(success: true));
        service.AddEntry(SampleDataBuilder.CreateHistoryEntry(success: false, errorMessage: "Failed"));
        service.AddEntry(SampleDataBuilder.CreateHistoryEntry(success: true));
        service.AddEntry(SampleDataBuilder.CreateHistoryEntry(success: false, errorMessage: "Another failure"));

        var failed = service.GetFailedOperations();
        failed.Should().HaveCount(2);
        failed.Should().OnlyContain(e => !e.Success);
    }

    [Fact]
    public void GetFailedOperations_NoFailures_ReturnsEmpty()
    {
        var service = CreateService();
        service.AddEntry(SampleDataBuilder.CreateHistoryEntry(success: true));

        var failed = service.GetFailedOperations();
        failed.Should().BeEmpty();
    }

    [Fact]
    public void ClearHistory_RemovesAllEntries()
    {
        var service = CreateService();
        service.AddEntry(SampleDataBuilder.CreateHistoryEntry());
        service.AddEntry(SampleDataBuilder.CreateHistoryEntry());

        service.ClearHistory();

        var history = service.GetHistory();
        history.Should().BeEmpty();
    }

    [Fact]
    public void GetHistory_Empty_ReturnsEmpty()
    {
        var service = CreateService();
        var history = service.GetHistory();
        history.Should().BeEmpty();
    }

    [Fact]
    public void AddEntry_PreservesEntryData()
    {
        var service = CreateService();
        var entry = SampleDataBuilder.CreateHistoryEntry(
            type: OperationType.Remove,
            projectName: "SpecialProject",
            success: false,
            errorMessage: "Package not found");

        service.AddEntry(entry);

        var history = service.GetHistory();
        history[0].Type.Should().Be(OperationType.Remove);
        history[0].ProjectName.Should().Be("SpecialProject");
        history[0].Success.Should().BeFalse();
        history[0].ErrorMessage.Should().Be("Package not found");
    }

    public void Dispose() => _temp.Dispose();
}
