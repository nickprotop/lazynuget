using FluentAssertions;
using LazyNuGet.Models;

namespace LazyNuGet.Tests.Models;

public class OperationResultTests
{
    [Fact]
    public void FromSuccess_SetsSuccessTrue()
    {
        var result = OperationResult.FromSuccess("done");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void FromSuccess_SetsMessage()
    {
        var result = OperationResult.FromSuccess("Package installed");
        result.Message.Should().Be("Package installed");
    }

    [Fact]
    public void FromSuccess_SetsExitCodeZero()
    {
        var result = OperationResult.FromSuccess("ok");
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public void FromSuccess_ErrorDetailsIsNull()
    {
        var result = OperationResult.FromSuccess("ok");
        result.ErrorDetails.Should().BeNull();
    }

    [Fact]
    public void FromError_SetsSuccessFalse()
    {
        var result = OperationResult.FromError("failed");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void FromError_SetsMessage()
    {
        var result = OperationResult.FromError("Package not found");
        result.Message.Should().Be("Package not found");
    }

    [Fact]
    public void FromError_SetsDetails()
    {
        var result = OperationResult.FromError("failed", "detailed error info");
        result.ErrorDetails.Should().Be("detailed error info");
    }

    [Fact]
    public void FromError_DefaultExitCodeIsOne()
    {
        var result = OperationResult.FromError("failed");
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public void FromError_CustomExitCode()
    {
        var result = OperationResult.FromError("failed", exitCode: 42);
        result.ExitCode.Should().Be(42);
    }

    [Fact]
    public void FromError_NullDetails_IsNull()
    {
        var result = OperationResult.FromError("failed");
        result.ErrorDetails.Should().BeNull();
    }
}
