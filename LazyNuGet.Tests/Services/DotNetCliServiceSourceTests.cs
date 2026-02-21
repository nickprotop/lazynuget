using FluentAssertions;
using LazyNuGet.Services;
using System.Runtime.InteropServices;

namespace LazyNuGet.Tests.Services;

/// <summary>
/// Tests argument construction for NuGet source management methods.
/// These tests validate command-line argument building without spawning real processes.
/// </summary>
public class DotNetCliServiceSourceTests
{
    // ── AddSourceAsync arg-building ──────────────────────────────────

    [Fact]
    public void BuildAddSourceArgs_WithCredentials_IncludesUsernameAndPassword()
    {
        var args = DotNetCliService.BuildAddSourceArgs(
            "MyFeed", "https://feed.example.com/v3/index.json", "user@example.com", "s3cr3t");

        args.Should().Contain("nuget add source");
        args.Should().Contain("https://feed.example.com/v3/index.json");
        args.Should().Contain("-n \"MyFeed\"");
        args.Should().Contain("-u \"user@example.com\"");
        args.Should().Contain("-p \"s3cr3t\"");
    }

    [Fact]
    public void BuildAddSourceArgs_NoCredentials_OmitsAuthArgs()
    {
        var args = DotNetCliService.BuildAddSourceArgs(
            "PublicFeed", "https://api.nuget.org/v3/index.json", null, null);

        args.Should().Contain("nuget add source");
        args.Should().Contain("-n \"PublicFeed\"");
        args.Should().NotContain("-u ");
        args.Should().NotContain("-p ");
    }

    [Fact]
    public void BuildAddSourceArgs_EmptyPassword_OmitsAuthArgs()
    {
        var args = DotNetCliService.BuildAddSourceArgs(
            "Feed", "https://feed.example.com", "user", "");

        args.Should().NotContain("-u ");
        args.Should().NotContain("-p ");
    }

    [Fact]
    public void BuildAddSourceArgs_EmptyUsername_OmitsAuthArgs()
    {
        var args = DotNetCliService.BuildAddSourceArgs(
            "Feed", "https://feed.example.com", "", "password");

        args.Should().NotContain("-u ");
        args.Should().NotContain("-p ");
    }

    [Fact]
    public void BuildAddSourceArgs_OnLinux_AppendsClearTextFlag()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return; // Skip on Windows — this test verifies Linux behaviour

        var args = DotNetCliService.BuildAddSourceArgs(
            "Feed", "https://feed.example.com", "user", "pass");

        args.Should().Contain("--store-password-in-clear-text");
    }

    [Fact]
    public void BuildAddSourceArgs_OnWindows_DoesNotAppendClearTextFlag()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return; // Skip on non-Windows

        var args = DotNetCliService.BuildAddSourceArgs(
            "Feed", "https://feed.example.com", "user", "pass");

        args.Should().NotContain("--store-password-in-clear-text");
    }

    // ── UpdateSourceAsync arg-building ──────────────────────────────────

    [Fact]
    public void BuildUpdateSourceArgs_WithNewPassword_BuildsCorrectArgs()
    {
        var args = DotNetCliService.BuildUpdateSourceArgs("MyFeed", "newuser", "newpass");

        args.Should().Contain("nuget update source");
        args.Should().Contain("\"MyFeed\"");
        args.Should().Contain("-u \"newuser\"");
        args.Should().Contain("-p \"newpass\"");
    }

    [Fact]
    public void BuildUpdateSourceArgs_NullPassword_OmitsAuthArgs()
    {
        var args = DotNetCliService.BuildUpdateSourceArgs("MyFeed", "user", null);

        args.Should().Contain("nuget update source");
        args.Should().Contain("\"MyFeed\"");
        args.Should().NotContain("-u ");
        args.Should().NotContain("-p ");
    }

    [Fact]
    public void BuildUpdateSourceArgs_BlankPassword_OmitsAuthArgs()
    {
        var args = DotNetCliService.BuildUpdateSourceArgs("MyFeed", "user", "");

        args.Should().NotContain("-u ");
        args.Should().NotContain("-p ");
    }

    [Fact]
    public void BuildUpdateSourceArgs_NoCredentials_JustNameInArgs()
    {
        var args = DotNetCliService.BuildUpdateSourceArgs("MyFeed", null, null);

        args.Should().Contain("nuget update source \"MyFeed\"");
        args.Should().NotContain("-u ");
        args.Should().NotContain("-p ");
    }

    // ── RemoveSourceAsync arg-building ──────────────────────────────────

    [Fact]
    public void BuildRemoveSourceArgs_BuildsCorrectArgs()
    {
        var args = DotNetCliService.BuildRemoveSourceArgs("MyFeed");

        args.Should().Be("nuget remove source \"MyFeed\"");
    }

    [Fact]
    public void BuildRemoveSourceArgs_NameWithSpaces_QuotesCorrectly()
    {
        var args = DotNetCliService.BuildRemoveSourceArgs("My Private Feed");

        args.Should().Be("nuget remove source \"My Private Feed\"");
    }
}
