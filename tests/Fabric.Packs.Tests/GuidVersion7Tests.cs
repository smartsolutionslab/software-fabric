namespace Fabric.Packs.Tests;

/// <summary>The base: every Guid is a time-ordered Guid v7, tests included; Guid.NewGuid does not compile in src/ or tests/.</summary>
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
    public void A_random_guid_in_a_module_test_project_fails_the_release_build()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("tests/Modules/Billing.Tests/RandomGuidTests.cs", """
            namespace Shop.Modules.Billing.Tests;

            public sealed class RandomGuidTests
            {
                [Fact]
                public void A_random_guid_is_not_empty()
                {
                    Guid.NewGuid().ShouldNotBe(Guid.Empty);
                }
            }
            """);

        var (exitCode, output) = project.TryBuild();

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("RandomGuidTests.cs");
        output.ShouldContain("RS0030");
        output.ShouldContain("'Guid.NewGuid()'");
        output.ShouldContain("Guid.CreateVersion7()");
    }

    [Fact]
    public void Src_and_tests_create_guids_with_version_7()
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
                public void An_invoice_id_wraps_the_guid_it_is_given()
                {
                    var value = Guid.CreateVersion7();

                    new InvoiceId(value).Value.ShouldBe(value);
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        exitCode.ShouldBe(0, output);
    }
}
