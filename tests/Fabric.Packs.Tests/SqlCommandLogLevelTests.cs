namespace Fabric.Packs.Tests;

/// <summary>
/// EF Core: a shipped appsettings that logs at Information does not thereby log every SQL statement. EF Core writes the
/// executed command, its text included, at Information under <c>Microsoft.EntityFrameworkCore.Database.Command</c>.
/// </summary>
public sealed class SqlCommandLogLevelTests
{
    private const string Profile = "saas-standard-react";

    private const string ArchitectureTests = "tests/Architecture.Tests/Architecture.Tests.csproj";

    private const string Rule = "SqlCommandsAreNotLoggedAtInformation";

    [Fact]
    public void The_generated_host_settings_keep_sql_commands_below_information_and_the_rule_passes()
    {
        using var project = new GeneratedProject(Profile);

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "Host", "appsettings.json"))
            .ShouldContain("\"Microsoft.EntityFrameworkCore.Database.Command\": \"Warning\"");
        File.Exists(Path.Combine(project.ProjectDirectory, "tests", "Architecture.Tests", "Rules", $"{Rule}.cs")).ShouldBeTrue();
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void Setting_the_sql_command_level_back_to_information_or_misspelling_it_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile);
        var settings = Path.Combine(project.ProjectDirectory, "src", "Host", "appsettings.json");
        var loud = File.ReadAllText(settings).Replace(
            "\"Microsoft.EntityFrameworkCore.Database.Command\": \"Warning\"",
            "\"Microsoft.EntityFrameworkCore.Database.Command\": \"Information\"",
            StringComparison.Ordinal);
        project.Write("src/Host/appsettings.json", loud);
        project.Write("src/Host/appsettings.Production.json", """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Debug",
                  "Microsoft.EntityFramework.Database.Command": "Warning"
                }
              }
            }
            """);
        project.Write("src/Host/appsettings.Staging.json", """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Warning"
                }
              }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/Host/appsettings.json: Default 'Information', 'Microsoft.EntityFrameworkCore.Database.Command' 'Information'");
        output.ShouldContain("src/Host/appsettings.Production.json: Default 'Debug', 'Microsoft.EntityFrameworkCore.Database.Command' not set");
        output.ShouldNotContain("src/Host/appsettings.Staging.json");
    }
}
