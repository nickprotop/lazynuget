using FluentAssertions;
using LazyNuGet.Orchestrators;

namespace LazyNuGet.Tests.Orchestrators;

public class PackageDetailsControllerTests
{
    private PackageDetailsController CreateController()
    {
        return new PackageDetailsController(
            null!,
            null,
            null,
            null,
            _ => { },
            () => null,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask);
    }

    [Fact]
    public void HandleDetailTabShortcut_F1_SetsOverviewTab()
    {
        var controller = CreateController();

        var result = controller.HandleDetailTabShortcut(ConsoleKey.F1);

        result.Should().BeTrue();
        controller.CurrentTab.Should().Be(PackageDetailTab.Overview);
    }

    [Fact]
    public void HandleDetailTabShortcut_F2_SetsDependenciesTab()
    {
        var controller = CreateController();

        var result = controller.HandleDetailTabShortcut(ConsoleKey.F2);

        result.Should().BeTrue();
        controller.CurrentTab.Should().Be(PackageDetailTab.Dependencies);
    }

    [Fact]
    public void HandleDetailTabShortcut_InvalidKey_ReturnsFalse()
    {
        var controller = CreateController();

        var result = controller.HandleDetailTabShortcut(ConsoleKey.A);

        result.Should().BeFalse();
        controller.CurrentTab.Should().Be(PackageDetailTab.Overview);
    }
}
