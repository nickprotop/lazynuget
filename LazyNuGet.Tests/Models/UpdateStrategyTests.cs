using FluentAssertions;
using LazyNuGet.Models;

namespace LazyNuGet.Tests.Models;

public class UpdateStrategyTests
{
    // --- GetDisplayName ---

    [Fact]
    public void GetDisplayName_UpdateAllToLatest_ReturnsExpected()
    {
        UpdateStrategy.UpdateAllToLatest.GetDisplayName()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetDisplayName_MinorAndPatchOnly_ReturnsExpected()
    {
        UpdateStrategy.MinorAndPatchOnly.GetDisplayName()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetDisplayName_PatchOnly_ReturnsExpected()
    {
        UpdateStrategy.PatchOnly.GetDisplayName()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetDisplayName_InvalidEnum_ReturnsNonEmpty()
    {
        ((UpdateStrategy)99).GetDisplayName()
            .Should().NotBeNullOrEmpty();
    }

    // --- GetDescription ---

    [Fact]
    public void GetDescription_UpdateAllToLatest_ReturnsExpected()
    {
        UpdateStrategy.UpdateAllToLatest.GetDescription()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetDescription_MinorAndPatchOnly_ReturnsExpected()
    {
        UpdateStrategy.MinorAndPatchOnly.GetDescription()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetDescription_PatchOnly_ReturnsExpected()
    {
        UpdateStrategy.PatchOnly.GetDescription()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetDescription_InvalidEnum_ReturnsEmpty()
    {
        ((UpdateStrategy)99).GetDescription()
            .Should().BeEmpty();
    }

    // --- GetShortcutKey ---

    [Fact]
    public void GetShortcutKey_UpdateAllToLatest_ReturnsExpected()
    {
        UpdateStrategy.UpdateAllToLatest.GetShortcutKey()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetShortcutKey_MinorAndPatchOnly_ReturnsExpected()
    {
        UpdateStrategy.MinorAndPatchOnly.GetShortcutKey()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetShortcutKey_PatchOnly_ReturnsExpected()
    {
        UpdateStrategy.PatchOnly.GetShortcutKey()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetShortcutKey_InvalidEnum_ReturnsEmpty()
    {
        ((UpdateStrategy)99).GetShortcutKey()
            .Should().BeEmpty();
    }
}
