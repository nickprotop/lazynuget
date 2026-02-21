using FluentAssertions;
using LazyNuGet.Services;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Services;

public class NuGetConfigServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _temp = new();
    private readonly NuGetConfigService _service = new();

    [Fact]
    public void GetEffectiveSources_SingleConfig_ReturnsSources()
    {
        var config = SampleDataBuilder.CreateNuGetConfig(
            ("nuget.org", "https://api.nuget.org/v3/index.json"),
            ("MyFeed", "https://myfeed.example.com/v3/index.json"));
        _temp.WriteFile("nuget.config", config);

        var sources = _service.GetEffectiveSources(_temp.Path);

        sources.Should().HaveCount(2);
        sources.Should().Contain(s => s.Name == "nuget.org");
        sources.Should().Contain(s => s.Name == "MyFeed");
    }

    [Fact]
    public void GetEffectiveSources_ClearDirective_RemovesPriorSources()
    {
        // Parent config with a source
        var parentDir = _temp.CreateSubDirectory("parent");
        var childDir = _temp.CreateSubDirectory("parent/child");

        _temp.WriteFile("parent/nuget.config", SampleDataBuilder.CreateNuGetConfig(
            ("nuget.org", "https://api.nuget.org/v3/index.json")));

        // Child config with <clear/>
        var childConfig = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <clear />
    <add key=""LocalOnly"" value=""https://local.example.com/v3/index.json"" />
  </packageSources>
</configuration>";
        _temp.WriteFile("parent/child/nuget.config", childConfig);

        var sources = _service.GetEffectiveSources(childDir);

        sources.Should().Contain(s => s.Name == "LocalOnly");
        sources.Should().NotContain(s => s.Name == "nuget.org");
    }

    [Fact]
    public void GetEffectiveSources_RemoveDirective_RemovesSpecificSource()
    {
        var parentDir = _temp.CreateSubDirectory("p");
        var childDir = _temp.CreateSubDirectory("p/c");

        _temp.WriteFile("p/nuget.config", SampleDataBuilder.CreateNuGetConfig(
            ("nuget.org", "https://api.nuget.org/v3/index.json"),
            ("PrivateFeed", "https://private.example.com/v3/index.json")));

        var childConfig = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <remove key=""PrivateFeed"" />
  </packageSources>
</configuration>";
        _temp.WriteFile("p/c/nuget.config", childConfig);

        var sources = _service.GetEffectiveSources(childDir);

        sources.Should().Contain(s => s.Name == "nuget.org");
        sources.Should().NotContain(s => s.Name == "PrivateFeed");
    }

    [Fact]
    public void GetEffectiveSources_DisabledSource_MarkedAsDisabled()
    {
        var config = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <add key=""nuget.org"" value=""https://api.nuget.org/v3/index.json"" />
  </packageSources>
  <disabledPackageSources>
    <add key=""nuget.org"" value=""true"" />
  </disabledPackageSources>
</configuration>";
        _temp.WriteFile("nuget.config", config);

        var sources = _service.GetEffectiveSources(_temp.Path);

        sources.Should().HaveCount(1);
        sources[0].IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void GetEffectiveSources_Credentials_Applied()
    {
        var config = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <clear />
    <add key=""PrivateFeed"" value=""https://private.example.com/v3/index.json"" />
  </packageSources>
  <packageSourceCredentials>
    <PrivateFeed>
      <add key=""Username"" value=""user@example.com"" />
      <add key=""ClearTextPassword"" value=""mypassword"" />
    </PrivateFeed>
  </packageSourceCredentials>
</configuration>";
        _temp.WriteFile("nuget.config", config);

        var sources = _service.GetEffectiveSources(_temp.Path);

        var privateFeed = sources.FirstOrDefault(s => s.Name == "PrivateFeed");
        privateFeed.Should().NotBeNull();
        privateFeed!.Username.Should().Be("user@example.com");
        privateFeed.ClearTextPassword.Should().Be("mypassword");
        privateFeed.RequiresAuth.Should().BeTrue();
    }

    [Fact]
    public void GetEffectiveSources_NoConfig_ReturnsEmptyOrUserLevel()
    {
        // Empty temp dir with no config files
        var emptyDir = _temp.CreateSubDirectory("empty");

        // Should not throw
        var sources = _service.GetEffectiveSources(emptyDir);
        sources.Should().NotBeNull();
    }

    [Fact]
    public void GetEffectiveSources_MalformedXml_DoesNotThrow()
    {
        _temp.WriteFile("nuget.config", "this is not xml <<<<");

        // Should not throw, just skip the malformed config
        Action act = () => _service.GetEffectiveSources(_temp.Path);
        act.Should().NotThrow();
    }

    [Fact]
    public void GetEffectiveSources_HierarchyMerging_CloserConfigOverrides()
    {
        var parentDir = _temp.CreateSubDirectory("h");
        var childDir = _temp.CreateSubDirectory("h/sub");

        // Parent: defines nuget.org with one URL
        _temp.WriteFile("h/nuget.config", SampleDataBuilder.CreateNuGetConfig(
            ("nuget.org", "https://parent.example.com/v3/index.json")));

        // Child: overrides nuget.org with different URL
        _temp.WriteFile("h/sub/nuget.config", SampleDataBuilder.CreateNuGetConfig(
            ("nuget.org", "https://child.example.com/v3/index.json")));

        var sources = _service.GetEffectiveSources(childDir);

        var nugetOrg = sources.FirstOrDefault(s => s.Name == "nuget.org");
        nugetOrg.Should().NotBeNull();
        nugetOrg!.Url.Should().Be("https://child.example.com/v3/index.json");
    }

    [Fact]
    public void GetConfigFilePaths_ReturnsDiscoveredPaths()
    {
        _temp.WriteFile("nuget.config", SampleDataBuilder.CreateNuGetConfig(
            ("test", "https://test.example.com")));

        var paths = _service.GetConfigFilePaths(_temp.Path);
        paths.Should().NotBeEmpty();
    }

    [Fact]
    public void GetEffectiveSources_SourcesHaveNuGetConfigOrigin()
    {
        _temp.WriteFile("nuget.config", SampleDataBuilder.CreateNuGetConfig(
            ("TestSource", "https://test.example.com")));

        var sources = _service.GetEffectiveSources(_temp.Path);

        var testSource = sources.FirstOrDefault(s => s.Name == "TestSource");
        testSource.Should().NotBeNull();
        testSource!.Origin.Should().Be(LazyNuGet.Models.NuGetSourceOrigin.NuGetConfig);
    }

    [Fact]
    public void GetEffectiveSources_DisabledFalse_SourceRemainsEnabled()
    {
        var config = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <add key=""nuget.org"" value=""https://api.nuget.org/v3/index.json"" />
  </packageSources>
  <disabledPackageSources>
    <add key=""nuget.org"" value=""false"" />
  </disabledPackageSources>
</configuration>";
        _temp.WriteFile("nuget.config", config);

        var sources = _service.GetEffectiveSources(_temp.Path);
        sources[0].IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void GetEffectiveSources_EmptyPackageSources_ReturnsEmpty()
    {
        var config = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
  </packageSources>
</configuration>";
        _temp.WriteFile("nuget.config", config);

        var sources = _service.GetEffectiveSources(_temp.Path);
        // May contain user-level sources, but at minimum should not throw
        sources.Should().NotBeNull();
    }

    [Fact]
    public void GetEffectiveSources_AddWithoutKey_Skipped()
    {
        var config = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <add value=""https://test.example.com"" />
    <add key=""Valid"" value=""https://valid.example.com"" />
  </packageSources>
</configuration>";
        _temp.WriteFile("nuget.config", config);

        var sources = _service.GetEffectiveSources(_temp.Path);
        sources.Should().Contain(s => s.Name == "Valid");
    }

    [Fact]
    public void GetEffectiveSources_XmlWithDtd_ThrowsOrSkips()
    {
        var dtdConfig = @"<?xml version=""1.0"" encoding=""utf-8""?>
<!DOCTYPE configuration [
  <!ENTITY xxe SYSTEM ""file:///etc/passwd"">
]>
<configuration>
  <packageSources>
    <add key=""evil"" value=""&xxe;"" />
  </packageSources>
</configuration>";
        _temp.WriteFile("nuget.config", dtdConfig);

        // Should not throw — the config with DTD should be skipped
        var sources = _service.GetEffectiveSources(_temp.Path);
        sources.Should().NotContain(s => s.Name == "evil");
    }

    [Fact]
    public void GetEffectiveSources_InvalidUrlScheme_SkipsSource()
    {
        var config = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <add key=""Valid"" value=""https://valid.example.com"" />
    <add key=""FtpSource"" value=""ftp://evil.example.com"" />
    <add key=""FileScheme"" value=""file:///tmp/packages"" />
  </packageSources>
</configuration>";
        _temp.WriteFile("nuget.config", config);

        var sources = _service.GetEffectiveSources(_temp.Path);
        sources.Should().Contain(s => s.Name == "Valid");
        sources.Should().NotContain(s => s.Name == "FtpSource");
        sources.Should().NotContain(s => s.Name == "FileScheme");
    }

    [Fact]
    public void GetEffectiveSources_ValidHttps_Accepted()
    {
        _temp.WriteFile("nuget.config", SampleDataBuilder.CreateNuGetConfig(
            ("SecureSource", "https://secure.example.com/v3/index.json")));

        var sources = _service.GetEffectiveSources(_temp.Path);
        sources.Should().Contain(s => s.Name == "SecureSource" && s.Url == "https://secure.example.com/v3/index.json");
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("/local/path", true)]
    [InlineData("C:\\local\\path", true)]
    [InlineData("ftp://evil.com", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValidSourceUrl_ValidatesCorrectly(string url, bool expected)
    {
        NuGetConfigService.IsValidSourceUrl(url).Should().Be(expected);
    }

    [Fact]
    public void GetEffectiveSources_PasswordKey_IgnoredOnLinux()
    {
        // A <Password> entry with base64 garbage (DPAPI-encrypted on Windows)
        // On Linux this must be silently ignored — no crash, no password set
        var config = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <clear />
    <add key=""PrivateFeed"" value=""https://private.example.com/v3/index.json"" />
  </packageSources>
  <packageSourceCredentials>
    <PrivateFeed>
      <add key=""Username"" value=""user@example.com"" />
      <add key=""Password"" value=""AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA..."" />
    </PrivateFeed>
  </packageSourceCredentials>
</configuration>";
        _temp.WriteFile("nuget.config", config);

        // Must not throw on any platform
        Action act = () => _service.GetEffectiveSources(_temp.Path);
        act.Should().NotThrow();

        var sources = _service.GetEffectiveSources(_temp.Path);
        var feed = sources.FirstOrDefault(s => s.Name == "PrivateFeed");
        feed.Should().NotBeNull();
        feed!.Username.Should().Be("user@example.com");

        // On Linux/macOS the DPAPI password is unreadable
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
        {
            feed.ClearTextPassword.Should().BeNull();
        }
    }

    [Fact]
    public void GetEffectiveSources_BothPasswordKeys_ClearTextTakesPrecedence()
    {
        // If both ClearTextPassword and Password exist, ClearTextPassword wins
        // (ClearTextPassword listed last in XML so it overwrites whatever Password set)
        var config = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <clear />
    <add key=""DualFeed"" value=""https://dual.example.com/v3/index.json"" />
  </packageSources>
  <packageSourceCredentials>
    <DualFeed>
      <add key=""Username"" value=""user"" />
      <add key=""Password"" value=""AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA..."" />
      <add key=""ClearTextPassword"" value=""clearpass"" />
    </DualFeed>
  </packageSourceCredentials>
</configuration>";
        _temp.WriteFile("nuget.config", config);

        var sources = _service.GetEffectiveSources(_temp.Path);
        var feed = sources.FirstOrDefault(s => s.Name == "DualFeed");
        feed.Should().NotBeNull();
        // ClearTextPassword comes after Password in the XML, so it wins
        feed!.ClearTextPassword.Should().Be("clearpass");
    }

    [Fact]
    public void GetEffectiveSources_NoCredentials_AuthNotRequired()
    {
        var config = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <packageSources>
    <add key=""PublicFeed"" value=""https://api.nuget.org/v3/index.json"" />
  </packageSources>
</configuration>";
        _temp.WriteFile("nuget.config", config);

        var sources = _service.GetEffectiveSources(_temp.Path);
        var feed = sources.FirstOrDefault(s => s.Name == "PublicFeed");
        feed.Should().NotBeNull();
        feed!.RequiresAuth.Should().BeFalse();
        feed.Username.Should().BeNull();
        feed.ClearTextPassword.Should().BeNull();
    }

    public void Dispose() => _temp.Dispose();
}
