using FluentAssertions;
using LazyNuGet.Orchestrators;

namespace LazyNuGet.Tests.Orchestrators;

public class PackageFilterControllerTests
{
    private PackageFilterController CreateController()
    {
        return new PackageFilterController(
            contextList: null,
            filterDisplay: null,
            leftPanelHeader: null,
            bottomHelpBar: null,
            statusBarManager: null,
            getCurrentViewState: () => ViewState.Packages,
            populatePackagesList: _ => { },
            updateDetailsContent: _ => { },
            handleSelectionChanged: () => { });
    }

    [Fact]
    public void EnterFilterMode_SetsFilterModeTrue()
    {
        var controller = CreateController();

        controller.EnterFilterMode();

        controller.IsFilterMode.Should().BeTrue();
    }

    [Fact]
    public void ExitFilterMode_ClearsFilterAndResetsMode()
    {
        var controller = CreateController();
        controller.EnterFilterMode();

        controller.ExitFilterMode();

        controller.IsFilterMode.Should().BeFalse();
        controller.FilterText.Should().BeEmpty();
    }

    [Fact]
    public void HandleFilterInput_Character_AppendsToFilter()
    {
        var controller = CreateController();
        controller.EnterFilterMode();

        controller.HandleFilterInput(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));

        controller.FilterText.Should().Be("a");
    }

    [Fact]
    public void HandleFilterInput_Backspace_RemovesLastCharacter()
    {
        var controller = CreateController();
        controller.EnterFilterMode();
        controller.HandleFilterInput(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
        controller.HandleFilterInput(new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false));

        controller.HandleFilterInput(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));

        controller.FilterText.Should().Be("a");
    }

    [Fact]
    public void HandleFilterInput_BackspaceOnEmpty_NoOp()
    {
        var controller = CreateController();
        controller.EnterFilterMode();

        controller.HandleFilterInput(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));

        controller.FilterText.Should().BeEmpty();
    }
}
