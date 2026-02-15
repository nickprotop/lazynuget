using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Tests.Helpers;
using LazyNuGet.UI.Utilities;

namespace LazyNuGet.Tests.UI;

public class PackageCheckProgressTrackerTests
{
    [Fact]
    public void IsActive_BeforeStart_ReturnsFalse()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_AfterStart_ReturnsTrue()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.Start(10);
        tracker.IsActive.Should().BeTrue();
    }

    [Fact]
    public void IsActive_AfterStop_ReturnsFalse()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.Start(10);
        tracker.Stop();
        tracker.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Start_ResetsCompletedCount()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.Start(10);
        tracker.IncrementCompleted();
        tracker.IncrementCompleted();

        // Re-start should reset
        tracker.Start(5);
        var (completed, total) = tracker.GetProgress();
        completed.Should().Be(0);
        total.Should().Be(5);
    }

    [Fact]
    public void IncrementCompleted_IncrementsCount()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.Start(10);
        tracker.IncrementCompleted();
        tracker.IncrementCompleted();
        tracker.IncrementCompleted();

        var (completed, _) = tracker.GetProgress();
        completed.Should().Be(3);
    }

    [Fact]
    public void IncrementCompleted_WhenNotActive_DoesNotIncrement()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.IncrementCompleted(); // Not started

        var (completed, _) = tracker.GetProgress();
        completed.Should().Be(0);
    }

    [Fact]
    public void GetProgress_ReturnsCorrectTuple()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.Start(50);
        tracker.IncrementCompleted();

        var (completed, total) = tracker.GetProgress();
        completed.Should().Be(1);
        total.Should().Be(50);
    }

    [Fact]
    public void GetProgressMessage_WhenNotActive_ReturnsDefaultMessage()
    {
        var tracker = new PackageCheckProgressTracker();
        var message = tracker.GetProgressMessage(0);
        message.Should().Contain("Checking for updates");
    }

    [Fact]
    public void GetProgressMessage_WhenActive_ContainsSpinner()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.Start(10);
        tracker.IncrementCompleted();

        var message = tracker.GetProgressMessage(0);
        message.Should().Contain("1/10");
    }

    [Fact]
    public void GetProgressMessage_ShowsPercentage()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.Start(100);
        for (int i = 0; i < 47; i++)
            tracker.IncrementCompleted();

        var message = tracker.GetProgressMessage(0);
        message.Should().Contain("47%");
    }

    [Fact]
    public void GetProgressMessage_SpinnerRotates()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.Start(10);

        var msg0 = tracker.GetProgressMessage(0);
        var msg1 = tracker.GetProgressMessage(1);

        // Different frames should produce different spinner characters
        msg0.Should().NotBe(msg1);
    }

    [Fact]
    public void GetProgressMessage_ZeroTotal_ReturnsDefaultMessage()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.Start(0);

        var message = tracker.GetProgressMessage(0);
        message.Should().Contain("Checking for updates");
    }

    [Fact]
    public async Task ThreadSafety_ConcurrentIncrements()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.Start(100);

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => tracker.IncrementCompleted()))
            .ToArray();

        await Task.WhenAll(tasks);

        var (completed, _) = tracker.GetProgress();
        completed.Should().Be(100);
    }

    [Fact]
    public void GetOutdatedCount_NullProjects_ReturnsZero()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.GetOutdatedCount(null!).Should().Be(0);
    }

    [Fact]
    public void GetOutdatedCount_EmptyProjects_ReturnsZero()
    {
        var tracker = new PackageCheckProgressTracker();
        tracker.GetOutdatedCount(new List<ProjectInfo>()).Should().Be(0);
    }

    [Fact]
    public void GetOutdatedCount_CountsAcrossProjects()
    {
        var tracker = new PackageCheckProgressTracker();
        var projects = new List<ProjectInfo>
        {
            SampleDataBuilder.CreateProjectInfo(packages: new List<PackageReference>
            {
                SampleDataBuilder.CreatePackageReference(version: "1.0.0", latestVersion: "2.0.0"),
                SampleDataBuilder.CreatePackageReference(id: "Serilog", latestVersion: null)
            }),
            SampleDataBuilder.CreateProjectInfo(name: "Project2", packages: new List<PackageReference>
            {
                SampleDataBuilder.CreatePackageReference(id: "xunit", version: "2.0.0", latestVersion: "3.0.0")
            })
        };

        tracker.GetOutdatedCount(projects).Should().Be(2);
    }

    [Fact]
    public void GetOutdatedCount_NoOutdated_ReturnsZero()
    {
        var tracker = new PackageCheckProgressTracker();
        var projects = new List<ProjectInfo>
        {
            SampleDataBuilder.CreateProjectInfo(packages: new List<PackageReference>
            {
                SampleDataBuilder.CreatePackageReference(latestVersion: null),
                SampleDataBuilder.CreatePackageReference(id: "Serilog", version: "3.0.0", latestVersion: "3.0.0")
            })
        };

        tracker.GetOutdatedCount(projects).Should().Be(0);
    }
}
