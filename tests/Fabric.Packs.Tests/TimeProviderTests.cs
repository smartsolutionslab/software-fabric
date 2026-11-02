namespace Fabric.Packs.Tests;

/// <summary>The base: code gets the current time from TimeProvider; reading the clock directly does not compile.</summary>
public sealed class TimeProviderTests
{
    private const string Profile = "saas-standard-react";

    [Fact]
    public void Reading_the_clock_directly_in_a_module_domain_fails_the_release_build()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Clock.cs", """
            namespace Shop.Modules.Billing.Domain;

            public static class Clock
            {
                public static DateTime Local() => DateTime.Now;

                public static DateTime Utc() => DateTime.UtcNow;

                public static DateTimeOffset LocalOffset() => DateTimeOffset.Now;

                public static DateTimeOffset UtcOffset() => DateTimeOffset.UtcNow;
            }
            """);

        var (exitCode, output) = project.TryBuild();

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("RS0030");
        output.ShouldContain("'DateTime.Now'");
        output.ShouldContain("'DateTime.UtcNow'");
        output.ShouldContain("'DateTimeOffset.Now'");
        output.ShouldContain("'DateTimeOffset.UtcNow'");
        output.ShouldContain("TimeProvider");
    }

    [Fact]
    public void A_domain_takes_a_time_provider_the_host_supplies_the_system_clock_and_tests_fake_it()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoice.cs", """
            namespace Shop.Modules.Billing.Domain;

            public sealed class Invoice(DateTimeOffset due)
            {
                public bool IsOverdue(TimeProvider time)
                {
                    Ensure.That(time).IsNotNull();
                    return time.GetUtcNow() > due;
                }
            }
            """);
        project.Write("tests/Modules/Billing.Tests/InvoiceTests.cs", """
            using Microsoft.Extensions.Time.Testing;
            using Shop.Modules.Billing.Domain;

            namespace Shop.Modules.Billing.Tests;

            public sealed class InvoiceTests
            {
                [Fact]
                public void An_invoice_is_overdue_once_its_due_date_has_passed()
                {
                    var due = new DateTimeOffset(2030, 1, 31, 0, 0, 0, TimeSpan.Zero);
                    var time = new FakeTimeProvider(due);
                    var invoice = new Invoice(due);

                    invoice.IsOverdue(time).ShouldBeFalse();
                    time.Advance(TimeSpan.FromDays(1));
                    invoice.IsOverdue(time).ShouldBeTrue();
                }
            }
            """);
        project.Write("tests/Architecture.Tests/ServiceDefaultsTimeTests.cs", """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Hosting;

            namespace Shop.Architecture.Tests;

            public sealed class ServiceDefaultsTimeTests
            {
                [Fact]
                public void The_service_defaults_supply_the_system_clock_as_the_time_provider()
                {
                    var builder = Host.CreateEmptyApplicationBuilder(settings: null);
                    builder.AddServiceDefaults();

                    using var provider = builder.Services.BuildServiceProvider();

                    provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(TimeProvider.System);
                }
            }
            """);
        project.Build();

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldBe(0, output);
    }
}
