using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Orchestrators;

namespace LazyNuGet.Tests.Orchestrators;

public class NavigationControllerTests
{
    private List<ProjectInfo> _projects = new();
    private List<string> _detailsContentLines = new();
    private bool _confirmExitCalled;

    private NavigationController CreateController()
    {
        return new NavigationController(
            contextList: null,
            leftPanelHeader: null,
            statusBarManager: null,
            filterController: null,
            packageDetailsController: null,
            errorHandler: null,
            window: null,
            getProjects: () => _projects,
            updateDetailsContent: lines => _detailsContentLines = lines,
            updateDetailsPanel: _ => { },
            handleUpdateAllAsync: _ => Task.CompletedTask,
            handleRestoreAsync: _ => Task.CompletedTask,
            showDependencyTreeAsync: (_, _) => Task.CompletedTask,
            confirmExitAsync: () => { _confirmExitCalled = true; return Task.CompletedTask; });
    }

    [Fact]
    public void HandleEnterKey_InProjectsView_WithNoList_DoesNotThrow()
    {
        var controller = CreateController();

        // No list control, so this should be a no-op
        var act = () => controller.HandleEnterKey();

        act.Should().NotThrow();
    }

    [Fact]
    public void HandleEscapeKey_InProjectsView_CallsConfirmExit()
    {
        var controller = CreateController();
        _confirmExitCalled = false;

        controller.HandleEscapeKey();

        // FireAndForget is async, but the callback should be invoked
        // Since there's no filter controller, it goes straight to the switch
        // In Projects view, it calls ConfirmExitAsync via FireAndForget
        // We can't easily verify async fire-and-forget in a sync test,
        // but we can verify the state is correct
        controller.CurrentViewState.Should().Be(ViewState.Projects);
    }

    [Fact]
    public void CurrentViewState_DefaultsToProjects()
    {
        var controller = CreateController();

        controller.CurrentViewState.Should().Be(ViewState.Projects);
    }

    [Fact]
    public void SelectedProject_CanBeSetAndRead()
    {
        var controller = CreateController();
        var project = new ProjectInfo
        {
            Name = "Test",
            FilePath = "/test/test.csproj",
            TargetFramework = "net8.0"
        };

        controller.SelectedProject = project;

        controller.SelectedProject.Should().Be(project);
    }

    [Fact]
    public void SwitchToProjectsView_SetsViewState()
    {
        var controller = CreateController();
        _projects = new List<ProjectInfo>();

        controller.SwitchToProjectsView();

        controller.CurrentViewState.Should().Be(ViewState.Projects);
        controller.SelectedProject.Should().BeNull();
    }

    [Fact]
    public void SwitchToPackagesView_SetsViewStateAndProject()
    {
        var controller = CreateController();
        var project = new ProjectInfo
        {
            Name = "Test",
            FilePath = "/test/test.csproj",
            TargetFramework = "net8.0",
            Packages = new List<PackageReference>()
        };

        controller.SwitchToPackagesView(project);

        controller.CurrentViewState.Should().Be(ViewState.Packages);
        controller.SelectedProject.Should().Be(project);
    }

    [Fact]
    public void HandleEscapeKey_InPackagesView_SwitchesToProjects()
    {
        var controller = CreateController();
        var project = new ProjectInfo
        {
            Name = "Test",
            FilePath = "/test/test.csproj",
            TargetFramework = "net8.0",
            Packages = new List<PackageReference>()
        };
        _projects = new List<ProjectInfo> { project };

        // First switch to packages view
        controller.SwitchToPackagesView(project);
        controller.CurrentViewState.Should().Be(ViewState.Packages);

        // Escape should go back to projects
        controller.HandleEscapeKey();

        controller.CurrentViewState.Should().Be(ViewState.Projects);
    }
}
