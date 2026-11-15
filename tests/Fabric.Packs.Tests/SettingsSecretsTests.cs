using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// The base's settings-secret rule: no committed <c>appsettings*.json</c> holds a key, a secret, a password or a token,
/// in every generated project, whether or not it calls a model, since every one has the same Keycloak, database and
/// broker settings. A violation names the file and the setting, never the value.
/// </summary>
public sealed class SettingsSecretsTests
{
    private const string Rule = "tests/Architecture.Tests/Rules/NoSecretInSettings.cs";

    private const string ModelOnlyRule = "tests/Architecture.Tests/Rules/NoApiKeyInSettings.cs";

    private const string ArchitectureTests = "tests/Architecture.Tests/Architecture.Tests.csproj";

    [Fact]
    public void Every_profile_gets_the_settings_secret_rule_whether_or_not_it_calls_a_model()
    {
        Repository.ProfilesChoosing("backend.llm", "none").ShouldNotBeEmpty();
        Repository.ProfilesNotChoosing("backend.llm", "none").ShouldNotBeEmpty();

        foreach (var profile in Repository.Catalog.Profiles.Keys)
        {
            var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, []);
            generated.Errors.ShouldBeEmpty(profile);
            var paths = generated.Files.Select(file => file.Path).ToList();
            paths.ShouldContain(Rule, profile);
            paths.ShouldNotContain(ModelOnlyRule, profile);
        }
    }

    [Fact]
    public void Without_a_model_a_secret_or_a_password_in_a_uri_in_a_settings_file_fails_the_architecture_tests_naming_the_file_and_the_setting()
    {
        using var project = new GeneratedProject("lean-crud");
        project.Write("src/Host/appsettings.Development.json", """
            {
              "Keycloak": {
                "ClientSecret": "not-a-real-client-secret",
                "TokenEndpoint": "http://localhost:8080/realms/shop/protocol/openid-connect/token"
              },
              "Payments": {
                "ApiKey": "not-a-real-payments-key"
              },
              "Reports": {
                "ConnectionString": "Host=db;Username=shop;Password=not-a-real-db-password"
              },
              "ConnectionStrings": {
                "messaging": "amqp://shop:not-a-real-broker-password@broker:5672",
                "cache": "redis://:not-a-real-cache-password@cache:6379",
                "guest": "amqp://guest@broker:5672",
                "placeholder": "amqp://shop:@broker:5672",
                "site": "http://localhost:5080/callback?from=a:b@c"
              },
              "Localization": {
                "DefaultCulture": "sk-SK"
              }
            }
            """);
        project.Write("src/AppHost/appsettings.Development.json", """
            {
              "Parameters": {
                "rabbitmq-password": "not-a-real-parameter-password"
              }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/Host/appsettings.Development.json: 'Keycloak:ClientSecret' holds a secret");
        output.ShouldContain("src/Host/appsettings.Development.json: 'Payments:ApiKey' holds an API key");
        output.ShouldContain("src/Host/appsettings.Development.json: 'Reports:ConnectionString' holds a password in a connection string");
        output.ShouldContain("src/Host/appsettings.Development.json: 'ConnectionStrings:messaging' holds a password in a URI");
        output.ShouldContain("src/Host/appsettings.Development.json: 'ConnectionStrings:cache' holds a password in a URI");
        output.ShouldContain("src/AppHost/appsettings.Development.json: 'Parameters:rabbitmq-password' holds a secret");
        output.ShouldNotContain("'Keycloak:TokenEndpoint'");
        output.ShouldNotContain("'ConnectionStrings:guest'");
        output.ShouldNotContain("'ConnectionStrings:placeholder'");
        output.ShouldNotContain("'ConnectionStrings:site'");
        output.ShouldNotContain("'Localization:DefaultCulture'");
        output.ShouldNotContain("not-a-real-");
    }
}
