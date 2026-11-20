namespace Fabric.Packs.Tests;

/// <summary>
/// EF Core: <c>AddModuleAdapters</c> registers a module's stores by convention, so a hand registration of the same
/// Application interface, which every project generated before the convention has, is a second registration that DI
/// resolves last-wins and silently (#845). The architecture rule <c>EveryModuleServiceIsRegisteredOnce</c> names it.
/// </summary>
public sealed class AdapterRegistrationTests
{
    private const string ArchitectureTests = "tests/Architecture.Tests/Architecture.Tests.csproj";

    private const string Rule = "EveryModuleServiceIsRegisteredOnce";

    private const string BillingModule = "src/Modules/Billing/Billing.Api/BillingModule.cs";

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("lean-crud")]
    public void A_module_as_generated_registers_each_of_its_services_once_and_the_rule_passes(string profile)
    {
        using var project = new GeneratedProject(profile);
        project.AddModule("billing");

        File.Exists(Path.Combine(project.ProjectDirectory, "tests", "Architecture.Tests", "Rules", $"{Rule}.cs")).ShouldBeTrue();
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_hand_registration_of_an_adapter_the_convention_registers_fails_the_architecture_tests_naming_both()
    {
        using var project = new GeneratedProject("saas-standard-react");
        project.AddModule("billing");
        var module = File.ReadAllText(Path.Combine(project.ProjectDirectory, BillingModule));
        module.ShouldContain("services.AddModuleInfrastructure<BillingModule>(configuration);");
        project.Write(BillingModule, "using Shop.Modules.Billing.Application;\nusing Shop.Modules.Billing.Infrastructure;\n" + module.Replace(
            "services.AddModuleInfrastructure<BillingModule>(configuration);",
            "services.AddModuleInfrastructure<BillingModule>(configuration).AddTransient<ISampleStore, SampleStore>();",
            StringComparison.Ordinal));
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain(
            "The module Billing registers Shop.Modules.Billing.Application.ISampleStore 2 times: "
            + "Shop.Modules.Billing.Infrastructure.SampleStore (Scoped), Shop.Modules.Billing.Infrastructure.SampleStore (Transient).");
    }
}
