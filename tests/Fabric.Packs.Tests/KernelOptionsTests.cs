namespace Fabric.Packs.Tests;

/// <summary>Guards, result style and absence: Smart Sentinel Eye's conventions by default, the plain ones on request.</summary>
public sealed class KernelOptionsTests
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
        project.Write("src/Modules/Billing/Billing.Application/InvoiceLookup.cs", InvoiceLookup);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldBe(0, output);
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
}
