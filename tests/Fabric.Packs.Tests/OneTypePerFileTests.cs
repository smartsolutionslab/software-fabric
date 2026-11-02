namespace Fabric.Packs.Tests;

/// <summary>
/// One type per file, named after the type (StyleCop SA1402 and SA1649), enforced by the Release build of a generated
/// project: a second top-level type of any kind fails it, a file named after nothing in it fails it, and the shapes the
/// rule allows (nested types, generic types in a file with the plain name, generated migrations) build.
/// </summary>
public sealed class OneTypePerFileTests
{
    private const string Profile = "lean-crud";
    private const string Domain = "src/Modules/Billing/Billing.Domain";
    private const string DomainProject = Domain + "/Billing.Domain.csproj";
    private const string TestProject = "tests/Modules/Billing.Tests/Billing.Tests.csproj";
    private const string InfrastructureProject = "src/Modules/Billing/Billing.Infrastructure/Billing.Infrastructure.csproj";

    public static TheoryData<string, string> SecondTopLevelTypes => new()
    {
        { "class", "public sealed class Stamp;" },
        { "record", "public sealed record Stamp(string Text);" },
        { "record struct", "public readonly record struct Stamp(int Value);" },
        { "struct", "public readonly struct Stamp;" },
        { "interface", "public interface IStamp\n{\n    int Value { get; }\n}" },
        { "enum", "public enum Stamp\n{\n    Paid,\n}" },
        { "delegate", "public delegate void Stamp();" },
    };

    [Theory]
    [MemberData(nameof(SecondTopLevelTypes))]
    public void A_second_top_level_type_in_a_file_fails_the_release_build(string kind, string declaration)
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write($"{Domain}/Ledger.cs", $$"""
            namespace Shop.Modules.Billing.Domain;

            public sealed class Ledger
            {
                public int Count { get; init; }
            }

            {{declaration}}
            """);

        var (exitCode, output) = BuildRelease(project, DomainProject);

        exitCode.ShouldNotBe(0, $"a second {kind}:\n{output}");
        output.ShouldContain("Ledger.cs(", Case.Sensitive, output);
        output.ShouldContain("error SA1402:", Case.Sensitive, output);
    }

    [Fact]
    public void A_generic_and_a_plain_type_of_the_same_name_in_one_file_fail_the_release_build()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write($"{Domain}/Pair.cs", """
            namespace Shop.Modules.Billing.Domain;

            public readonly record struct Pair<T>(T Left, T Right);

            public static class Pair
            {
                public static Pair<T> Of<T>(T left, T right) => new(left, right);
            }
            """);

        var (exitCode, output) = BuildRelease(project, DomainProject);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Pair.cs(", Case.Sensitive, output);
        output.ShouldContain("error SA1402:", Case.Sensitive, output);
    }

    [Fact]
    public void A_second_top_level_type_in_a_test_file_fails_the_release_build()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("tests/Modules/Billing.Tests/LedgerTests.cs", """
            namespace Shop.Modules.Billing.Tests;

            public sealed class LedgerTests
            {
                [Fact]
                public void One_and_one_make_two() => (1 + 1).ShouldBe(LedgerExpectations.Two);
            }

            internal static class LedgerExpectations
            {
                public const int Two = 2;
            }
            """);

        var (exitCode, output) = BuildRelease(project, TestProject);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("LedgerTests.cs(", Case.Sensitive, output);
        output.ShouldContain("error SA1402:", Case.Sensitive, output);
    }

    [Fact]
    public void A_file_not_named_after_its_type_fails_the_release_build()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write($"{Domain}/Ledgers.cs", """
            namespace Shop.Modules.Billing.Domain;

            public sealed class Ledger
            {
                public int Count { get; init; }
            }
            """);

        var (exitCode, output) = BuildRelease(project, DomainProject);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Ledgers.cs(", Case.Sensitive, output);
        output.ShouldContain("error SA1649:", Case.Sensitive, output);
    }

    [Fact]
    public void Nested_types_and_a_generic_type_in_a_file_with_its_plain_name_build()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write($"{Domain}/LedgerFailures.cs", """
            namespace Shop.Modules.Billing.Domain;

            public static class LedgerFailures
            {
                public static string Describe(int balance) => new Overdrawn(balance, Severity.Warning).ToString();

                private enum Severity
                {
                    Warning,
                }

                private sealed record Overdrawn(int Balance, Severity Severity);
            }
            """);
        project.Write($"{Domain}/Box.cs", """
            namespace Shop.Modules.Billing.Domain;

            public sealed class Box<T>
            {
                public required T Content { get; init; }
            }
            """);

        var (exitCode, output) = BuildRelease(project, DomainProject);

        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void Generated_migrations_may_hold_several_types()
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
                    }
                }

                /// <inheritdoc />
                public partial class Second : Migration
                {
                    /// <inheritdoc />
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                    }
                }
            }
            """);

        var (exitCode, output) = BuildRelease(project, InfrastructureProject);

        exitCode.ShouldBe(0, output);
    }

    private static (int ExitCode, string Output) BuildRelease(GeneratedProject project, string csproj) =>
        project.DotNetCommand(new Dictionary<string, string>(), "build", csproj, "-c", "Release", "-nodeReuse:false");
}
