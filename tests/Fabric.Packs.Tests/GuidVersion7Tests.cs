namespace Fabric.Packs.Tests;

/// <summary>The base: new identifiers in src/ are time-ordered Guid v7; Guid.NewGuid does not compile there.</summary>
public sealed class GuidVersion7Tests
{
    private const string Profile = "saas-standard-react";

    [Fact]
    public void A_random_guid_in_a_module_domain_fails_the_release_build()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/InvoiceId.cs", """
            namespace Shop.Modules.Billing.Domain;

            public sealed record InvoiceId(Guid Value)
            {
                public static InvoiceId New() => new(Guid.NewGuid());
            }
            """);

        var (exitCode, output) = project.TryBuild();

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("RS0030");
        output.ShouldContain("'Guid.NewGuid()'");
        output.ShouldContain("Guid.CreateVersion7()");
    }

    [Fact]
    public void Src_creates_identifiers_with_guid_v7_and_tests_may_still_use_random_guids()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/InvoiceId.cs", """
            namespace Shop.Modules.Billing.Domain;

            public sealed record InvoiceId(Guid Value)
            {
                public static InvoiceId New() => new(Guid.CreateVersion7());
            }
            """);
        project.Write("tests/Modules/Billing.Tests/InvoiceIdTests.cs", """
            using Shop.Modules.Billing.Domain;

            namespace Shop.Modules.Billing.Tests;

            public sealed class InvoiceIdTests
            {
                [Fact]
                public void A_new_invoice_id_is_a_version_7_guid()
                {
                    InvoiceId.New().Value.Version.ShouldBe(7);
                }

                [Fact]
                public void An_invoice_id_wraps_any_guid_it_is_given()
                {
                    var value = Guid.NewGuid();

                    new InvoiceId(value).Value.ShouldBe(value);
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        exitCode.ShouldBe(0, output);
    }
}
