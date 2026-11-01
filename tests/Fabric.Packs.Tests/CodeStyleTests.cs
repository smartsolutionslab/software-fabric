namespace Fabric.Packs.Tests;

/// <summary>
/// The code style in the generated .editorconfig, enforced by the Release build (warnings are errors): one planted
/// violation per rule family, the shapes the rules allow, and the code they leave alone.
/// </summary>
public sealed class CodeStyleTests
{
    private const string Profile = "lean-crud";
    private const string Domain = "src/Modules/Billing/Billing.Domain";
    private const string DomainProject = Domain + "/Billing.Domain.csproj";
    private const string TestProject = "tests/Modules/Billing.Tests/Billing.Tests.csproj";

    public static TheoryData<string, string, string> Violations => new()
    {
        {
            "IDE0007", "ExplicitLocal.cs", """
                namespace Shop.Modules.Billing.Domain;

                public static class ExplicitLocal
                {
                    public static int Twice(int value)
                    {
                        int doubled = value * 2;
                        return doubled;
                    }
                }
                """
        },
        {
            "IDE0300", "ArrayCreation.cs", """
                namespace Shop.Modules.Billing.Domain;

                public static class ArrayCreation
                {
                    public static int[] Numbers() => new int[] { 1, 2, 3 };
                }
                """
        },
        {
            "IDE0161", "BlockNamespace.cs", """
                namespace Shop.Modules.Billing.Domain
                {
                    public static class BlockNamespace
                    {
                        public const int Answer = 42;
                    }
                }
                """
        },
        {
            "IDE0011", "NoBraces.cs", """
                namespace Shop.Modules.Billing.Domain;

                public static class NoBraces
                {
                    public static int Positive(int value)
                    {
                        if (value < 0)
                            return -value;

                        return value;
                    }
                }
                """
        },
        {
            "IDE0130", "WrongNamespace.cs", """
                namespace Shop.Modules.Elsewhere;

                public static class WrongNamespace
                {
                    public const int Answer = 42;
                }
                """
        },
        {
            "IDE0044", "WritableField.cs", """
                namespace Shop.Modules.Billing.Domain;

                public sealed class WritableField
                {
                    private int count = 1;

                    public int Count() => count;
                }
                """
        },
        {
            "IDE1006", "UnderscoreField.cs", """
                namespace Shop.Modules.Billing.Domain;

                public sealed class UnderscoreField(int count)
                {
                    private readonly int _count = count;

                    public int Count() => _count;
                }
                """
        },
        {
            "IDE1006", "AsyncWithoutSuffix.cs", """
                namespace Shop.Modules.Billing.Domain;

                public static class AsyncWithoutSuffix
                {
                    public static async Task Pause() => await Task.Yield();
                }
                """
        },
        {
            "IDE1006", "InterfaceWithoutPrefix.cs", """
                namespace Shop.Modules.Billing.Domain;

                public interface Clock
                {
                    DateTimeOffset Now { get; }
                }
                """
        },
    };

    [Theory]
    [MemberData(nameof(Violations))]
    public void A_style_violation_in_a_module_fails_the_release_build(string diagnostic, string file, string content)
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write($"{Domain}/{file}", content);

        var (exitCode, output) = BuildRelease(project, DomainProject);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain($"{file}(", Case.Sensitive, output);
        output.ShouldContain($"error {diagnostic}:", Case.Sensitive, output);
    }

    /// <summary>
    /// IDE0049 comes from Roslyn's name simplifier, which runs in dotnet format but not in dotnet build; the format
    /// check in the generated CI is where it fails.
    /// </summary>
    [Fact]
    public void A_framework_type_name_fails_the_format_check()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write($"{Domain}/FrameworkTypeName.cs", """
            namespace Shop.Modules.Billing.Domain;

            public static class FrameworkTypeName
            {
                public static Int32 Echo(Int32 value) => value;
            }

            """);

        var (exitCode, output) = FormattingTests.RunFormatCheck(project);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("FrameworkTypeName.cs(", Case.Sensitive, output);
        output.ShouldContain("error IDE0049:", Case.Sensitive, output);
    }

    [Fact]
    public void An_underscore_field_in_a_test_project_fails_the_release_build()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("tests/Modules/Billing.Tests/UnderscoreFieldTests.cs", """
            namespace Shop.Modules.Billing.Tests;

            public sealed class UnderscoreFieldTests
            {
                private readonly int _expected = 2;

                [Fact]
                public void One_and_one_make_two() => (1 + 1).ShouldBe(_expected);
            }
            """);

        var (exitCode, output) = BuildRelease(project, TestProject);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("UnderscoreFieldTests.cs(", Case.Sensitive, output);
        output.ShouldContain("error IDE1006:", Case.Sensitive, output);
    }

    [Fact]
    public void The_field_shapes_the_rules_allow_build()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write($"{Domain}/Counter.cs", """
            namespace Shop.Modules.Billing.Domain;

            public sealed class Counter
            {
                private const int Start = 1;
                private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
                private readonly int step;
                private int value = Start;

                public Counter(int step)
                {
                    this.step = step;
                }

                public TimeSpan Every => Window * step;

                public int Next()
                {
                    value += step;
                    return value;
                }

                public static async Task<int> NextAsync(Counter counter)
                {
                    await Task.Yield();
                    return counter.Next();
                }
            }
            """);

        var (exitCode, output) = BuildRelease(project, DomainProject);

        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void Test_methods_are_sentences_and_need_no_async_suffix()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("tests/Modules/Billing.Tests/SentenceTests.cs", """
            namespace Shop.Modules.Billing.Tests;

            public sealed class SentenceTests
            {
                [Fact]
                public async Task A_yield_completes()
                {
                    await Task.Yield();
                    true.ShouldBeTrue();
                }
            }
            """);

        var (exitCode, output) = BuildRelease(project, TestProject);

        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void Generated_migrations_are_left_alone()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Infrastructure/Persistence/Migrations/20260101000000_Initial.cs", """
            using Microsoft.EntityFrameworkCore.Migrations;

            #nullable disable

            namespace Shop.Modules.Billing.Infrastructure.Persistence.Migrations
            {
                /// <inheritdoc />
                public partial class Initial : Migration
                {
                    /// <inheritdoc />
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.InsertData(
                            schema: "billing",
                            table: "notes",
                            columns: new[] { "id", "text" },
                            values: new object[] { 1, "first" });
                    }

                    /// <inheritdoc />
                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        String table = "notes";
                        migrationBuilder.DeleteData(schema: "billing", table: table, keyColumn: "id", keyValue: 1);
                    }
                }
            }
            """);

        var (exitCode, output) = BuildRelease(project, "src/Modules/Billing/Billing.Infrastructure/Billing.Infrastructure.csproj");

        exitCode.ShouldBe(0, output);
    }

    [Theory]
    [MemberData(nameof(FormattingTests.Profiles), MemberType = typeof(FormattingTests))]
    public void A_fresh_project_of_every_profile_with_a_module_builds_in_release(string profile)
    {
        using var project = new GeneratedProject(profile, FormattingTests.FrontendStandIns.GetValueOrDefault(profile, []));
        project.AddModule("billing");

        project.Build();
    }

    private static (int ExitCode, string Output) BuildRelease(GeneratedProject project, string csproj) =>
        project.DotNetCommand(new Dictionary<string, string>(), "build", csproj, "-c", "Release", "-nodeReuse:false");
}
