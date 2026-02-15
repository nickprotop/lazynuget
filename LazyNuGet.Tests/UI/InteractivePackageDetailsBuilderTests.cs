using FluentAssertions;
using LazyNuGet.UI.Components;

namespace LazyNuGet.Tests.UI;

public class InteractivePackageDetailsBuilderTests
{
    // --- DetectPotentialBreakingChange ---

    [Fact]
    public void DetectBreakingChange_MajorBump_ReturnsTrue()
    {
        InteractivePackageDetailsBuilder.DetectPotentialBreakingChange("1.0.0", "2.0.0")
            .Should().BeTrue();
    }

    [Fact]
    public void DetectBreakingChange_MinorBump_ReturnsFalse()
    {
        InteractivePackageDetailsBuilder.DetectPotentialBreakingChange("1.0.0", "1.1.0")
            .Should().BeFalse();
    }

    [Fact]
    public void DetectBreakingChange_PatchBump_ReturnsFalse()
    {
        InteractivePackageDetailsBuilder.DetectPotentialBreakingChange("1.0.0", "1.0.1")
            .Should().BeFalse();
    }

    [Fact]
    public void DetectBreakingChange_SameVersion_ReturnsFalse()
    {
        InteractivePackageDetailsBuilder.DetectPotentialBreakingChange("1.0.0", "1.0.0")
            .Should().BeFalse();
    }

    [Fact]
    public void DetectBreakingChange_InvalidInstalled_ReturnsFalse()
    {
        InteractivePackageDetailsBuilder.DetectPotentialBreakingChange("invalid", "2.0.0")
            .Should().BeFalse();
    }

    [Fact]
    public void DetectBreakingChange_InvalidLatest_ReturnsFalse()
    {
        InteractivePackageDetailsBuilder.DetectPotentialBreakingChange("1.0.0", "invalid")
            .Should().BeFalse();
    }

    [Fact]
    public void DetectBreakingChange_LargeJump_ReturnsTrue()
    {
        InteractivePackageDetailsBuilder.DetectPotentialBreakingChange("1.0.0", "5.0.0")
            .Should().BeTrue();
    }

    // --- GetMajorVersion ---

    [Fact]
    public void GetMajorVersion_ValidVersion_ReturnsMajor()
    {
        InteractivePackageDetailsBuilder.GetMajorVersion("3.2.1")
            .Should().Be(3);
    }

    [Fact]
    public void GetMajorVersion_NoDots_ParsesAsNumber()
    {
        InteractivePackageDetailsBuilder.GetMajorVersion("7")
            .Should().Be(7);
    }

    [Fact]
    public void GetMajorVersion_Empty_ReturnsNegative()
    {
        InteractivePackageDetailsBuilder.GetMajorVersion("")
            .Should().Be(-1);
    }

    [Fact]
    public void GetMajorVersion_Null_ReturnsNegative()
    {
        InteractivePackageDetailsBuilder.GetMajorVersion(null!)
            .Should().Be(-1);
    }

    [Fact]
    public void GetMajorVersion_NonNumeric_ReturnsNegative()
    {
        InteractivePackageDetailsBuilder.GetMajorVersion("abc.1.2")
            .Should().Be(-1);
    }

    [Fact]
    public void GetMajorVersion_ZeroMajor()
    {
        InteractivePackageDetailsBuilder.GetMajorVersion("0.1.0")
            .Should().Be(0);
    }

    // --- CountIntermediateVersions ---

    [Fact]
    public void CountIntermediate_BothPresent_ReturnsCorrectCount()
    {
        var versions = new List<string> { "3.0.0", "2.1.0", "2.0.0", "1.1.0", "1.0.0" };

        InteractivePackageDetailsBuilder.CountIntermediateVersions(versions, "1.0.0", "3.0.0")
            .Should().Be(3); // 2.1.0, 2.0.0, 1.1.0
    }

    [Fact]
    public void CountIntermediate_AdjacentVersions_ReturnsZero()
    {
        var versions = new List<string> { "2.0.0", "1.0.0" };

        InteractivePackageDetailsBuilder.CountIntermediateVersions(versions, "1.0.0", "2.0.0")
            .Should().Be(0);
    }

    [Fact]
    public void CountIntermediate_InstalledNotInList_ReturnsZero()
    {
        var versions = new List<string> { "3.0.0", "2.0.0" };

        InteractivePackageDetailsBuilder.CountIntermediateVersions(versions, "1.0.0", "3.0.0")
            .Should().Be(0);
    }

    [Fact]
    public void CountIntermediate_LatestNotInList_ReturnsZero()
    {
        var versions = new List<string> { "2.0.0", "1.0.0" };

        InteractivePackageDetailsBuilder.CountIntermediateVersions(versions, "1.0.0", "3.0.0")
            .Should().Be(0);
    }

    [Fact]
    public void CountIntermediate_SameVersion_ReturnsZero()
    {
        var versions = new List<string> { "1.0.0" };

        InteractivePackageDetailsBuilder.CountIntermediateVersions(versions, "1.0.0", "1.0.0")
            .Should().Be(0);
    }

    // --- FormatDownloads ---

    [Fact]
    public void FormatDownloads_BelowThousand_ReturnsRaw()
    {
        InteractivePackageDetailsBuilder.FormatDownloads(999)
            .Should().Be("999");
    }

    [Fact]
    public void FormatDownloads_Thousands_ReturnsK()
    {
        InteractivePackageDetailsBuilder.FormatDownloads(1_500)
            .Should().Be("1.5K");
    }

    [Fact]
    public void FormatDownloads_Millions_ReturnsM()
    {
        InteractivePackageDetailsBuilder.FormatDownloads(2_500_000)
            .Should().Be("2.5M");
    }

    [Fact]
    public void FormatDownloads_Billions_ReturnsB()
    {
        InteractivePackageDetailsBuilder.FormatDownloads(1_500_000_000)
            .Should().Be("1.5B");
    }

    [Fact]
    public void FormatDownloads_ExactThreshold_1000()
    {
        InteractivePackageDetailsBuilder.FormatDownloads(1_000)
            .Should().Be("1.0K");
    }

    [Fact]
    public void FormatDownloads_Zero_ReturnsZero()
    {
        InteractivePackageDetailsBuilder.FormatDownloads(0)
            .Should().Be("0");
    }

    // --- FormatSize ---

    [Fact]
    public void FormatSize_Bytes_ReturnsBytes()
    {
        InteractivePackageDetailsBuilder.FormatSize(512)
            .Should().Be("512 bytes");
    }

    [Fact]
    public void FormatSize_KB()
    {
        InteractivePackageDetailsBuilder.FormatSize(1_024)
            .Should().Be("1.00 KB");
    }

    [Fact]
    public void FormatSize_MB()
    {
        InteractivePackageDetailsBuilder.FormatSize(1_048_576)
            .Should().Be("1.00 MB");
    }

    [Fact]
    public void FormatSize_GB()
    {
        InteractivePackageDetailsBuilder.FormatSize(1_073_741_824)
            .Should().Be("1.00 GB");
    }

    [Fact]
    public void FormatSize_FractionalKB()
    {
        InteractivePackageDetailsBuilder.FormatSize(1_536) // 1.5 KB
            .Should().Be("1.50 KB");
    }

    [Fact]
    public void FormatSize_ZeroBytes()
    {
        InteractivePackageDetailsBuilder.FormatSize(0)
            .Should().Be("0 bytes");
    }
}
