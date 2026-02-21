using FluentAssertions;
using LazyNuGet.Services;
using LazyNuGet.Tests.Helpers;

namespace LazyNuGet.Tests.Services;

public class ConfigurationServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _temp = new();

    private ConfigurationService CreateService()
    {
        return new ConfigurationService(_temp.Path);
    }

    [Fact]
    public void Load_NoConfigFile_ReturnsDefaults()
    {
        var service = CreateService();
        var settings = service.Load();

        settings.Should().NotBeNull();
        settings.LastFolderPath.Should().BeNull();
        settings.RecentFolders.Should().BeEmpty();
        settings.CustomSources.Should().BeEmpty();
        settings.SourceOverrides.Should().BeEmpty();
        settings.ShowWelcomeOnStartup.Should().BeTrue();
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var service = CreateService();
        var settings = new LazyNuGetSettings
        {
            LastFolderPath = "/home/user/projects",
            ShowWelcomeOnStartup = false
        };
        settings.RecentFolders.Add("/home/user/projects");
        settings.RecentFolders.Add("/home/user/other");

        service.Save(settings);
        var loaded = service.Load();

        loaded.LastFolderPath.Should().Be("/home/user/projects");
        loaded.ShowWelcomeOnStartup.Should().BeFalse();
        loaded.RecentFolders.Should().HaveCount(2);
    }

    [Fact]
    public void TrackFolder_SetsLastFolderPath()
    {
        var service = CreateService();
        service.TrackFolder("/home/user/myproject");

        var settings = service.Load();
        settings.LastFolderPath.Should().Be("/home/user/myproject");
    }

    [Fact]
    public void TrackFolder_AddsToRecentFolders()
    {
        var service = CreateService();
        service.TrackFolder("/home/user/project1");
        service.TrackFolder("/home/user/project2");

        var settings = service.Load();
        settings.RecentFolders.Should().HaveCount(2);
        settings.RecentFolders[0].Should().Be("/home/user/project2"); // Most recent first
    }

    [Fact]
    public void TrackFolder_DeduplicatesExistingFolder()
    {
        var service = CreateService();
        service.TrackFolder("/home/user/project1");
        service.TrackFolder("/home/user/project2");
        service.TrackFolder("/home/user/project1"); // Track again

        var settings = service.Load();
        settings.RecentFolders.Should().HaveCount(2);
        settings.RecentFolders[0].Should().Be("/home/user/project1"); // Moved to front
    }

    [Fact]
    public void TrackFolder_CapsAt10()
    {
        var service = CreateService();

        for (int i = 0; i < 15; i++)
        {
            service.TrackFolder($"/home/user/project{i}");
        }

        var settings = service.Load();
        settings.RecentFolders.Should().HaveCount(10);
        settings.RecentFolders[0].Should().Be("/home/user/project14"); // Most recent
    }

    [Fact]
    public void Save_CreatesDirectory()
    {
        // Use a nested path that doesn't exist yet
        var nestedDir = Path.Combine(_temp.Path, "nested", "config");
        var service = new ConfigurationService(nestedDir);

        service.Save(new LazyNuGetSettings { LastFolderPath = "/test" });

        Directory.Exists(nestedDir).Should().BeTrue();
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        var service = CreateService();
        _temp.WriteFile("settings.json", "{{{{corrupt json}}}}");

        var settings = service.Load();
        settings.Should().NotBeNull();
        settings.LastFolderPath.Should().BeNull();
    }

    [Fact]
    public void Save_PreservesCustomSources()
    {
        var service = CreateService();
        var settings = new LazyNuGetSettings();
        settings.CustomSources.Add(new CustomNuGetSource
        {
            Name = "MyFeed",
            Url = "https://myfeed.example.com",
            IsEnabled = true
        });

        service.Save(settings);
        var loaded = service.Load();

        loaded.CustomSources.Should().HaveCount(1);
        loaded.CustomSources[0].Name.Should().Be("MyFeed");
        loaded.CustomSources[0].Url.Should().Be("https://myfeed.example.com");
    }

    [Fact]
    public void Save_PreservesSourceOverrides()
    {
        var service = CreateService();
        var settings = new LazyNuGetSettings();
        settings.SourceOverrides["nuget.org"] = false;

        service.Save(settings);
        var loaded = service.Load();

        loaded.SourceOverrides.Should().ContainKey("nuget.org");
        loaded.SourceOverrides["nuget.org"].Should().BeFalse();
    }

    [Fact]
    public void Load_OversizedFile_ReturnsDefaults()
    {
        var service = CreateService();

        // Create a file larger than 1MB
        var oversizedContent = new string('x', 1_048_577);
        _temp.WriteFile("settings.json", oversizedContent);

        var settings = service.Load();
        settings.Should().NotBeNull();
        settings.LastFolderPath.Should().BeNull();
    }

    [Fact]
    public void DefaultConstructor_UsesSystemConfigDir()
    {
        // Verify the default constructor doesn't throw
        var service = new ConfigurationService();
        var settings = service.Load();
        settings.Should().NotBeNull();
    }

    [Fact]
    public void CustomNuGetSource_ClearTextPassword_NotWrittenWhenNull()
    {
        var service = CreateService();
        var settings = new LazyNuGetSettings();
        settings.CustomSources.Add(new CustomNuGetSource
        {
            Name = "MyFeed",
            Url = "https://feed.example.com",
            ClearTextPassword = null  // null → WhenWritingNull → omitted from JSON
        });

        service.Save(settings);

        var json = File.ReadAllText(Path.Combine(_temp.Path, "settings.json"));
        json.Should().NotContain("ClearTextPassword");
    }

    [Fact]
    public void CustomNuGetSource_ClearTextPassword_ReadFromOldJson()
    {
        // Simulate a pre-migration settings.json that still has ClearTextPassword
        var oldJson = """
            {
              "CustomSources": [
                {
                  "Name": "OldFeed",
                  "Url": "https://old.example.com",
                  "IsEnabled": true,
                  "RequiresAuth": true,
                  "Username": "user",
                  "ClearTextPassword": "secret"
                }
              ]
            }
            """;
        _temp.WriteFile("settings.json", oldJson);

        var service = CreateService();
        var settings = service.Load();

        settings.CustomSources.Should().HaveCount(1);
        settings.CustomSources[0].ClearTextPassword.Should().Be("secret");
    }

    [Fact]
    public void CustomNuGetSource_ClearTextPassword_AfterNullSet_DisappearsFromJson()
    {
        // Start with a settings.json containing ClearTextPassword
        var oldJson = """
            {
              "CustomSources": [
                {
                  "Name": "MigFeed",
                  "Url": "https://mig.example.com",
                  "IsEnabled": true,
                  "RequiresAuth": true,
                  "Username": "admin",
                  "ClearTextPassword": "p@ssw0rd"
                }
              ]
            }
            """;
        _temp.WriteFile("settings.json", oldJson);

        var service = CreateService();
        var settings = service.Load();

        // Simulate migration: null out the password and re-save
        settings.CustomSources[0].ClearTextPassword = null;
        service.Save(settings);

        var json = File.ReadAllText(Path.Combine(_temp.Path, "settings.json"));
        json.Should().NotContain("ClearTextPassword");

        // Reload and verify
        var reloaded = service.Load();
        reloaded.CustomSources[0].ClearTextPassword.Should().BeNull();
    }

    public void Dispose() => _temp.Dispose();
}
