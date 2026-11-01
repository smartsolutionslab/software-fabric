namespace Fabric.Packs.Tests;

/// <summary>The persistence.interceptors slot of backend.data-access.ef-core.</summary>
public sealed class PersistenceInterceptorTests
{
    [Fact]
    public void An_interceptor_another_pack_contributes_is_added_to_every_module_context_without_a_new_service_provider_each_time()
    {
        // contract-tests has no pack yet; the probe takes its place and fills the slot.
        using var probe = new ProbePack("backend.test.extras.contract-tests", new Dictionary<string, string>
        {
            ["persistence.interceptors"] = "new Shop.BuildingBlocks.Persistence.ProbeInterceptor(Services)",
        });
        using var project = new GeneratedProject("lean-crud", [probe.Manifest], "backend.test.extras=contract-tests");
        project.AddModule("billing");
        project.Write("src/BuildingBlocks/Persistence/ProbeInterceptor.cs", """
            using Microsoft.EntityFrameworkCore.Diagnostics;

            namespace Shop.BuildingBlocks.Persistence;

            public sealed class ProbeInterceptor(IServiceProvider services) : SaveChangesInterceptor
            {
                public IServiceProvider Services { get; } = services;
            }
            """);
        project.Write("tests/Modules/Billing.Tests/InterceptorTests.cs", """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Infrastructure;
            using Shop.BuildingBlocks.Persistence;
            using Shop.Modules.Billing.Infrastructure.Persistence;

            namespace Shop.Modules.Billing.Tests;

            public class InterceptorTests
            {
                [Fact]
                public void Every_context_instance_gets_its_own_interceptor_with_its_services()
                {
                    // EF Core fails a process that builds more than twenty internal service providers; interceptors must not.
                    for (var instance = 0; instance < 25; instance++)
                    {
                        using var context = new BillingDbContextFactory().CreateDbContext([]);
                        context.Model.ShouldNotBeNull();

                        var interceptor = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()
                            .ShouldNotBeNull().Interceptors.ShouldNotBeNull().OfType<ProbeInterceptor>().ShouldHaveSingleItem();
                        interceptor.Services.ShouldBeSameAs(context.Services);
                    }
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void Without_an_interceptor_the_module_context_does_not_override_OnConfiguring()
    {
        using var project = new GeneratedProject("lean-crud");

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "BuildingBlocks", "Persistence", "ModuleDbContext.cs"))
            .ShouldNotContain("OnConfiguring");
    }
}
