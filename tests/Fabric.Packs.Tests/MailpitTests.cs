using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// Keycloak's email reaches Mailpit when the AppHost runs; publishing the AppHost adds no Mailpit. Mailpit is the base's
/// resource (#861), so that e-mail can have it under every auth option.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class MailpitTests
{
    private const string PublishModeTests = """
        using Aspire.Hosting;
        using Aspire.Hosting.Testing;

        namespace Shop.Integration.Tests;

        public class PublishModeTests
        {
            [Fact]
            public async Task Publishing_the_apphost_adds_no_mailpit()
            {
                await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
                    ["--operation", "publish", "--publisher", "manifest", "--output-path", Path.GetTempPath()],
                    TestContext.Current.CancellationToken);

                builder.ExecutionContext.IsPublishMode.ShouldBeTrue();
                var resources = builder.Resources.Select(resource => resource.Name).ToList();
                resources.ShouldContain("keycloak");
                resources.ShouldNotContain("mailpit");
            }
        }
        """;

    [Fact]
    public void The_base_adds_mailpit_to_the_apphost_and_no_option_pack_does()
    {
        var adding = Repository.Packs.Packs.Values
            .SelectMany(pack => pack.Slots
                .Where(contribution => contribution.Slot == "apphost.resources")
                .Where(contribution => File.ReadAllText(Path.Combine(pack.Directory, contribution.Template)).Contains("\"mailpit\"", StringComparison.Ordinal))
                .Select(_ => pack.Option))
            .ToList();

        adding.ShouldBe(["base.core"]);
    }

    [Fact]
    public void Under_keycloak_mailpit_runs_in_run_mode_and_keycloak_waits_for_it_as_before()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", "saas-standard-react", []);
        generated.Errors.ShouldBeEmpty();
        var appHost = generated.Files.Single(file => file.Path == "src/AppHost/AppHost.cs").Content;

        var runMode = appHost.IndexOf("if (builder.ExecutionContext.IsRunMode)", StringComparison.Ordinal);
        runMode.ShouldBeGreaterThan(appHost.IndexOf("var keycloak = ", StringComparison.Ordinal));
        appHost.IndexOf("builder.AddContainer(\"mailpit\", \"axllent/mailpit\", \"v1.33\")", StringComparison.Ordinal).ShouldBeGreaterThan(runMode);
        appHost.ShouldContain("keycloak.WaitFor(mailpit);");
    }

    [Fact]
    public void A_reset_password_email_reaches_mailpit_in_run_mode_and_publish_mode_has_no_mailpit()
    {
        using var project = new GeneratedProject("saas-standard-react");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "tests", "Integration.Tests", "HostTests.cs"))
            .ShouldContain("A_reset_password_email_from_the_realm_reaches_mailpit");
        project.Write("tests/Integration.Tests/PublishModeTests.cs", PublishModeTests);
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 12");
    }
}
