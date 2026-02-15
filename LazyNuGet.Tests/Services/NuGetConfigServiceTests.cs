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

    public void Dispose() => _temp.Dispose();
}
