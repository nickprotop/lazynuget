using FluentAssertions;
using LazyNuGet.Models;
using LazyNuGet.Services;

namespace LazyNuGet.Tests.Services;

public class VersionComparisonServiceTests
{
    // --- UpdateAllToLatest strategy ---

    [Fact]
    public void IsUpdateAllowed_UpdateAll_MajorBump_ReturnsTrue()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0", "2.0.0", UpdateStrategy.UpdateAllToLatest)
            .Should().BeTrue();
    }

    [Fact]
    public void IsUpdateAllowed_UpdateAll_MinorBump_ReturnsTrue()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0", "1.1.0", UpdateStrategy.UpdateAllToLatest)
            .Should().BeTrue();
    }

    [Fact]
    public void IsUpdateAllowed_UpdateAll_PatchBump_ReturnsTrue()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0", "1.0.1", UpdateStrategy.UpdateAllToLatest)
            .Should().BeTrue();
    }

    // --- MinorAndPatchOnly strategy ---

    [Fact]
    public void IsUpdateAllowed_MinorPatch_MajorBump_ReturnsFalse()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0", "2.0.0", UpdateStrategy.MinorAndPatchOnly)
            .Should().BeFalse();
    }

    [Fact]
    public void IsUpdateAllowed_MinorPatch_MinorBump_ReturnsTrue()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0", "1.1.0", UpdateStrategy.MinorAndPatchOnly)
            .Should().BeTrue();
    }

    [Fact]
    public void IsUpdateAllowed_MinorPatch_PatchBump_ReturnsTrue()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0", "1.0.1", UpdateStrategy.MinorAndPatchOnly)
            .Should().BeTrue();
    }

    // --- PatchOnly strategy ---

    [Fact]
    public void IsUpdateAllowed_PatchOnly_MajorBump_ReturnsFalse()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0", "2.0.0", UpdateStrategy.PatchOnly)
            .Should().BeFalse();
    }

    [Fact]
    public void IsUpdateAllowed_PatchOnly_MinorBump_ReturnsFalse()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0", "1.1.0", UpdateStrategy.PatchOnly)
            .Should().BeFalse();
    }

    [Fact]
    public void IsUpdateAllowed_PatchOnly_PatchBump_ReturnsTrue()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0", "1.0.1", UpdateStrategy.PatchOnly)
            .Should().BeTrue();
    }

    // --- Edge cases ---

    [Fact]
    public void IsUpdateAllowed_EqualVersions_ReturnsFalse()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0", "1.0.0", UpdateStrategy.UpdateAllToLatest)
            .Should().BeFalse();
    }

    [Fact]
    public void IsUpdateAllowed_DowngradeAttempt_ReturnsFalse()
    {
        VersionComparisonService.IsUpdateAllowed("2.0.0", "1.0.0", UpdateStrategy.UpdateAllToLatest)
            .Should().BeFalse();
    }

    [Fact]
    public void IsUpdateAllowed_InvalidCurrentVersion_ReturnsFalse()
    {
        VersionComparisonService.IsUpdateAllowed("not-a-version", "1.0.0", UpdateStrategy.UpdateAllToLatest)
            .Should().BeFalse();
    }

    [Fact]
    public void IsUpdateAllowed_InvalidLatestVersion_ReturnsFalse()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0", "not-a-version", UpdateStrategy.UpdateAllToLatest)
            .Should().BeFalse();
    }

    [Fact]
    public void IsUpdateAllowed_EmptyCurrentVersion_ReturnsFalse()
    {
        VersionComparisonService.IsUpdateAllowed("", "1.0.0", UpdateStrategy.UpdateAllToLatest)
            .Should().BeFalse();
    }

    [Fact]
    public void IsUpdateAllowed_EmptyLatestVersion_ReturnsFalse()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0", "", UpdateStrategy.UpdateAllToLatest)
            .Should().BeFalse();
    }

    [Fact]
    public void IsUpdateAllowed_PrereleaseVersion_AllowsUpdate()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0-beta", "1.0.0", UpdateStrategy.UpdateAllToLatest)
            .Should().BeTrue();
    }

    [Fact]
    public void IsUpdateAllowed_PrereleaseToPrerelease_AllowsUpdate()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0-alpha", "1.0.0-beta", UpdateStrategy.UpdateAllToLatest)
            .Should().BeTrue();
    }

    [Fact]
    public void IsUpdateAllowed_InvalidStrategyEnum_ReturnsFalse()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0", "2.0.0", (UpdateStrategy)99)
            .Should().BeFalse();
    }

    [Fact]
    public void IsUpdateAllowed_FourPartVersion_Works()
    {
        VersionComparisonService.IsUpdateAllowed("1.0.0.0", "1.0.1.0", UpdateStrategy.PatchOnly)
            .Should().BeTrue();
    }
}
