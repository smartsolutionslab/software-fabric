using System.Text.RegularExpressions;

namespace Fabric.Packs.Tests;

/// <summary>Guards, result style and absence: Smart Sentinel Eye's conventions by default, the plain ones on request.</summary>
public sealed partial class KernelOptionsTests
{
    private const string Profile = "saas-standard-react";

    private const string InvoiceLookup = """
        using System.Net;

        namespace Shop.Modules.Billing.Application;

        public sealed record InvoiceNotFound(Guid InvoiceId)
            : ApiError("billing.invoice-not-found", "No such invoice.", HttpStatusCode.NotFound);

        public static class InvoiceLookup
        {
            public static Result<string, ApiError> Find(Guid invoiceId, Option<string> number)
            {
                Ensure.That(invoiceId).IsNotEmpty();
                if (!number.HasValue)
                {
                    return Failure<ApiError>(new InvoiceNotFound(invoiceId));
                }

                return Success(number.Value);
            }
        }
        """;

    [Fact]
    public void Smart_sentinel_eye_conventions_are_the_default_and_a_handler_can_use_them()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application", InvoiceLookup);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void A_switch_on_a_result_or_an_option_that_misses_a_case_does_not_compile()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/InvoiceDescriptions.cs", """
            namespace Shop.Modules.Billing.Application;

            public static class InvoiceDescriptions
            {
                public static string Describe(Result<string, ApiError> result) =>
                    result switch
                    {
                        SuccessOutcome<string> success => success.Value,
                        FailureOutcome<ApiError> failure => failure.Error.Code,
                    };

                public static string Describe(Option<string> number) =>
                    number switch
                    {
                        Some<string> some => some.Value,
                        None => "none",
                    };

                public static string Forget(Result<string, ApiError> result) =>
                    result switch
                    {
                        SuccessOutcome<string> success => success.Value,
                    };

                public static string Forget(Option<string> number) =>
                    number switch
                    {
                        Some<string> some => some.Value,
                    };
            }
            """);

        var (exitCode, output) = project.TryBuild();

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("the pattern 'Shop.Kernel.FailureOutcome<Shop.Kernel.ApiError>' is not covered");
        output.ShouldContain("the pattern 'Shop.Kernel.None' is not covered");
        Errors(output).ShouldBe(["CS8509"], output);
    }

    [Fact]
    public void A_switch_on_a_minimal_result_that_misses_a_case_does_not_compile()
    {
        using var project = new GeneratedProject(Profile, "backend.result-style=minimal");
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/InvoiceDescriptions.cs", """
            namespace Shop.Modules.Billing.Application;

            public static class InvoiceDescriptions
            {
                public static string Describe(Result<string, ApiError> result) =>
                    result switch
                    {
                        SuccessOutcome<string> success => success.Value,
                        FailureOutcome<ApiError> failure => failure.Error.Code,
                    };

                public static string Forget(Result<string, ApiError> result) =>
                    result switch
                    {
                        FailureOutcome<ApiError> failure => failure.Error.Code,
                    };
            }
            """);

        var (exitCode, output) = project.TryBuild();

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("the pattern 'Shop.Kernel.SuccessOutcome<string>' is not covered");
        Errors(output).ShouldBe(["CS8509"], output);
    }

    [Fact]
    public void The_bcl_guards_are_banned_when_ensure_that_is_chosen()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/Guarded.cs", """
            namespace Shop.Modules.Billing.Application;

            public static class Guarded
            {
                public static string Echo(string text)
                {
                    ArgumentNullException.ThrowIfNull(text);
                    return text;
                }
            }
            """);

        var (exitCode, output) = project.TryBuild();

        exitCode.ShouldNotBe(0);
        output.ShouldContain("RS0030");
        output.ShouldContain("Ensure.That");
    }

    [Fact]
    public void A_project_that_stops_reading_the_guard_ban_list_fails_the_architecture_tests_by_name()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/Billing.Application.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <AdditionalFiles Remove="@(AdditionalFiles)" />
              </ItemGroup>
            </Project>
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/Modules/Billing/Billing.Application/Billing.Application.csproj does not read build/guards/BannedSymbols.txt");
        output.ShouldNotContain("Billing.Domain.csproj");
        output.ShouldNotContain("Kernel.csproj");
    }

    [Fact]
    public void A_project_that_stops_reading_the_guard_ban_list_only_in_the_tested_configuration_is_named()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/Billing.Application.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup Condition="'$(Configuration)' == 'Release'">
                <AdditionalFiles Remove="@(AdditionalFiles)" />
              </ItemGroup>
            </Project>
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/Modules/Billing/Billing.Application/Billing.Application.csproj does not read build/guards/BannedSymbols.txt");
    }

    [Fact]
    public void A_project_that_stops_reading_the_guard_ban_list_only_in_another_configuration_passes()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/Billing.Application.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup Condition="'$(Configuration)' == 'Debug'">
                <AdditionalFiles Remove="@(AdditionalFiles)" />
              </ItemGroup>
            </Project>
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void A_nullable_return_from_application_code_breaks_the_absence_rule()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/Lookup.cs", """
            namespace Shop.Modules.Billing.Application;

            public static class Lookup
            {
                public static string? FindNumber(Guid invoiceId) => invoiceId == Guid.Empty ? null : "INV-1";
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Lookup.FindNumber returns a nullable reference; return Option<T>.");
    }

    [Fact]
    public void The_plain_alternatives_build_and_pass_their_tests()
    {
        using var project = new GeneratedProject(Profile, "backend.guards=bcl", "backend.result-style=minimal", "backend.absence=nullable");
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/Lookup.cs", """
            namespace Shop.Modules.Billing.Application;

            public static class Lookup
            {
                public static Result<string, ApiError> Find(string? number)
                {
                    ArgumentNullException.ThrowIfNull(number);
                    return Result<string, ApiError>.Success(number);
                }
            }
            """);
        project.Build();

        project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj").ExitCode.ShouldBe(0);
        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void Minimal_results_with_ensure_that_guards_build_and_pass_their_tests()
    {
        using var project = new GeneratedProject(Profile, "backend.result-style=minimal");
        project.AddModule("billing");
        project.Build();

        var (resultsExitCode, resultsOutput) = project.Test("tests/Kernel/Results.Tests/Results.Tests.csproj");
        resultsExitCode.ShouldBe(0, resultsOutput);
        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldBe(0, output);
    }

    /// <summary>The distinct compiler and analyzer error codes in a build's output.</summary>
    private static string[] Errors(string output) =>
        [.. ErrorCode().Matches(output).Select(match => match.Groups["code"].Value).Distinct()];

    [GeneratedRegex("error (?<code>[A-Z]+[0-9]+):")]
    private static partial Regex ErrorCode();
}
