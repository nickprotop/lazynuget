using FluentAssertions;
using LazyNuGet.Services;

namespace LazyNuGet.Tests.Services;

public class DotNetCliServiceParsingTests
{
    [Fact]
    public void Parse_SingleFramework_TopLevelPackages()
    {
        var output = @"Project 'TestProject' has the following package references
   [net9.0]:
   Top-level Package      Requested   Resolved
   > Newtonsoft.Json       13.0.1      13.0.1
   > Serilog               3.1.1       3.1.1
";
        var result = DotNetCliService.ParseTransitiveDependencyOutput(output, "/src/TestProject.csproj");

        result.Should().HaveCount(1);
        result[0].TargetFramework.Should().Be("net9.0");
        result[0].TopLevelPackages.Should().HaveCount(2);
        result[0].TopLevelPackages[0].PackageId.Should().Be("Newtonsoft.Json");
        result[0].TopLevelPackages[0].RequestedVersion.Should().Be("13.0.1");
        result[0].TopLevelPackages[0].ResolvedVersion.Should().Be("13.0.1");
        result[0].TopLevelPackages[0].IsTransitive.Should().BeFalse();
    }

    [Fact]
    public void Parse_TransitivePackages()
    {
        var output = @"Project 'TestProject' has the following package references
   [net9.0]:
   Top-level Package      Requested   Resolved
   > Newtonsoft.Json       13.0.1      13.0.1

   Transitive Package               Resolved
   > System.Runtime.CompilerServices.Unsafe   6.0.0
";
        var result = DotNetCliService.ParseTransitiveDependencyOutput(output, "/src/TestProject.csproj");

        result[0].TransitivePackages.Should().HaveCount(1);
        result[0].TransitivePackages[0].PackageId.Should().Be("System.Runtime.CompilerServices.Unsafe");
        result[0].TransitivePackages[0].ResolvedVersion.Should().Be("6.0.0");
        result[0].TransitivePackages[0].IsTransitive.Should().BeTrue();
    }

    [Fact]
    public void Parse_MultipleFrameworks()
    {
        var output = @"Project 'TestProject' has the following package references
   [net8.0]:
   Top-level Package      Requested   Resolved
   > xunit                 2.9.0       2.9.0

   [net9.0]:
   Top-level Package      Requested   Resolved
   > xunit                 2.9.3       2.9.3
";
        var result = DotNetCliService.ParseTransitiveDependencyOutput(output, "/src/TestProject.csproj");

        result.Should().HaveCount(2);
        result[0].TargetFramework.Should().Be("net8.0");
        result[1].TargetFramework.Should().Be("net9.0");
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsEmpty()
    {
        var result = DotNetCliService.ParseTransitiveDependencyOutput("", "/src/TestProject.csproj");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NoPackageLines_ReturnsTreeWithEmptyLists()
    {
        var output = @"Project 'TestProject' has the following package references
   [net9.0]:
   Top-level Package      Requested   Resolved
";
        var result = DotNetCliService.ParseTransitiveDependencyOutput(output, "/src/TestProject.csproj");

        result.Should().HaveCount(1);
        result[0].TopLevelPackages.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ProjectName_ExtractedFromPath()
    {
        var output = @"   [net9.0]:
   Top-level Package      Requested   Resolved
   > xunit                 2.9.0       2.9.0
";
        var result = DotNetCliService.ParseTransitiveDependencyOutput(output, "/src/MyApp/MyApp.csproj");
        result[0].ProjectName.Should().Be("MyApp");
    }

    [Fact]
    public void Parse_RealWorldOutput()
    {
        var output = @"Project 'LazyNuGet' has the following package references
   [net9.0]:
   Top-level Package                Requested   Resolved
   > NuGet.Versioning                7.3.0       7.3.0
   > SharpConsoleUI                  2.4.24      2.4.24
   > System.Text.Json                9.0.0       9.0.0

   Transitive Package                           Resolved
   > Microsoft.CSharp                            4.7.0
   > Microsoft.NETCore.Platforms                  7.0.0
";
        var result = DotNetCliService.ParseTransitiveDependencyOutput(output, "/home/nick/source/lazynuget/LazyNuGet.csproj");

        result.Should().HaveCount(1);
        result[0].TopLevelPackages.Should().HaveCount(3);
        result[0].TransitivePackages.Should().HaveCount(2);
        result[0].ProjectName.Should().Be("LazyNuGet");
    }

    [Fact]
    public void Parse_NoFrameworkSection_ReturnsEmpty()
    {
        var output = "Some random text\nwithout framework sections\n";
        var result = DotNetCliService.ParseTransitiveDependencyOutput(output, "/src/Test.csproj");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_OnlyTransitiveSection()
    {
        var output = @"   [net9.0]:
   Transitive Package               Resolved
   > System.Memory                    4.5.5
   > System.Buffers                   4.5.1
";
        var result = DotNetCliService.ParseTransitiveDependencyOutput(output, "/src/Test.csproj");

        result[0].TopLevelPackages.Should().BeEmpty();
        result[0].TransitivePackages.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_LinesWithoutMarker_AreIgnored()
    {
        var output = @"   [net9.0]:
   Top-level Package      Requested   Resolved
   > xunit                 2.9.0       2.9.0
   Some random line without > marker
   > Serilog               3.0.0       3.0.0
";
        var result = DotNetCliService.ParseTransitiveDependencyOutput(output, "/src/Test.csproj");
        result[0].TopLevelPackages.Should().HaveCount(2);
    }
}
