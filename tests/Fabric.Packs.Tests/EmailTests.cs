using Fabric.Core.Catalog;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.email: smtp</c> (generator ADR-0019): e-mail over SMTP with MailKit, behind <c>IEmailSender</c> in
/// <c>src/BuildingBlocks/Email</c>, shaped after fabric-pilot's hand-written sending. In run mode the host sends to the
/// base's Mailpit, unencrypted; when publishing, the AppHost declares the SMTP sign-in as parameters. No profile chooses
/// it, so no profile changes but for the Mailpit declaration R3 moves out of the run-mode block.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class EmailTests
{
    private const string Profile = "lean-crud";

    private const string Smtp = "backend.email=smtp";

    private const string ArchitectureTests = "tests/Architecture.Tests/Architecture.Tests.csproj";

    private static readonly string[] EmailFiles =
    [
        "src/BuildingBlocks/Email/IEmailSender.cs",
        "src/BuildingBlocks/Email/EmailMessage.cs",
        "src/BuildingBlocks/Email/SmtpSettings.cs",
        "src/BuildingBlocks/Email/SmtpCredentials.cs",
        "src/BuildingBlocks/Email/MailKitEmailSender.cs",
        "src/BuildingBlocks/Email/Wiring/EmailRegistration.cs",
        "src/AppHost/Wiring/EmailWiring.cs",
        "tests/Fakes/FakeEmailSender.cs",
        "tests/Architecture.Tests/Rules/OnlyTheEmailWiringNamesMailKit.cs",
        "tests/Architecture.Tests/Rules/NoSmtpPasswordInSettings.cs",
        "tests/Email.Tests/Email.Tests.csproj",
        "tests/Email.Tests/SmtpSettingsTests.cs",
        "tests/Email.Tests/MailKitEmailSenderTests.cs",
        "tests/Integration.Tests/EmailTests.cs",
    ];

    private const string ApplicationNamingMimeKit = """
        using MimeKit;

        namespace Shop.Modules.Billing.Application;

        public sealed class DirectMail
        {
            public MimeMessage Message { get; } = new();
        }
        """;

    private const string ApplicationSendingThroughTheBuildingBlock = """
        using Shop.BuildingBlocks.Email;

        namespace Shop.Modules.Billing.Application;

        public sealed class InvoiceMailer(IEmailSender sender)
        {
            public Task SendAsync(string to, CancellationToken cancellationToken) =>
                sender.SendAsync(new EmailMessage(to, "Your invoice", "It is attached."), cancellationToken);
        }
        """;

    [Fact]
    public void The_dimension_is_project_scoped_changeable_and_none_by_default_with_smtp_as_its_one_other_option()
    {
        var email = Repository.Catalog.Dimensions["backend.email"];

        email.Scope.ShouldBe(DimensionScope.Project);
        email.Changeable.ShouldBeTrue();
        email.Default.ShouldBe(["none"]);
        email.Options.Keys.ShouldBe(["none", "smtp"]);
        Repository.Packs.Packs.Keys.ShouldContain("backend.email.smtp");
        Repository.Packs.Packs.Keys.ShouldNotContain("backend.email.none");
    }

    [Fact]
    public void A_project_sending_email_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Smtp]);

        generated.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_building_block_the_fake_and_the_gates_come_with_smtp_and_no_profile_gets_them()
    {
        var files = Files(Profile, Smtp);
        foreach (var file in EmailFiles)
        {
            files.Keys.ShouldContain(file);
        }

        Repository.ProfilesNotChoosing("backend.email", "none").ShouldBeEmpty();
        foreach (var profile in Repository.Catalog.Profiles.Keys)
        {
            var paths = Files(profile).Keys.ToList();
            paths.ShouldNotContain(path => EmailFiles.Contains(path), profile);
            paths.ShouldNotContain("docs/adr/fabric/backend-email-none.md", profile);
        }
    }

    [Fact]
    public void Mailkit_is_a_package_of_the_building_blocks_and_the_host_registers_the_sender()
    {
        var files = Files(Profile, Smtp);

        files["Directory.Packages.props"].ShouldContain("<PackageVersion Include=\"MailKit\" Version=\"5.0.0\" />");
        files["src/BuildingBlocks/BuildingBlocks.csproj"].ShouldContain("<PackageReference Include=\"MailKit\" />");
        files["src/Host/Program.cs"].ShouldContain("builder.Services.AddEmail(builder.Configuration);");
        files["src/BuildingBlocks/Email/Wiring/EmailRegistration.cs"].ShouldContain("public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)");
        files["src/BuildingBlocks/Email/IEmailSender.cs"].ShouldContain("Task SendAsync(EmailMessage message, CancellationToken cancellationToken);");
        files["docs/adr/fabric/backend-email-smtp.md"].ShouldContain("MailKit");
        files["AGENTS.md"].ShouldContain("## E-mail: SMTP");
    }

    [Fact]
    public void In_run_mode_the_host_sends_to_mailpit_unencrypted_and_publishing_declares_the_smtp_sign_in_as_parameters()
    {
        var files = Files(Profile, Smtp);
        var appHost = files["src/AppHost/AppHost.cs"];

        var declared = appHost.IndexOf("IResourceBuilder<ContainerResource>? mailpit = null;", StringComparison.Ordinal);
        declared.ShouldBeGreaterThan(0);
        appHost.IndexOf("if (builder.ExecutionContext.IsRunMode)", StringComparison.Ordinal).ShouldBeGreaterThan(declared);
        appHost.ShouldContain("    mailpit = builder.AddContainer(\"mailpit\", \"axllent/mailpit\", \"v1.33\")");
        appHost.ShouldContain("keycloak.WaitFor(mailpit);");
        appHost.ShouldContain("var smtpUserName = mailpit is null ? builder.AddParameter(\"smtp-user-name\") : null;");
        appHost.ShouldContain("var smtpPassword = mailpit is null ? builder.AddParameter(\"smtp-password\", secret: true) : null;");
        appHost.ShouldContain(".WithEmail(mailpit, smtpUserName, smtpPassword)");

        var wiring = files["src/AppHost/Wiring/EmailWiring.cs"];
        wiring.ShouldContain("\"ConnectionStrings__mail\"");
        wiring.ShouldContain(".WithEnvironment(\"Email__SenderAddress\", \"noreply@example.com\")");
        wiring.ShouldContain(".WithEnvironment(\"Email__AllowUnencryptedSmtp\", \"true\")");
        wiring.ShouldContain(".WaitFor(mailpit)");
        wiring.ShouldContain(".WithEnvironment(\"Email__SmtpUserName\", smtpUserName)");
        wiring.ShouldContain(".WithEnvironment(\"Email__SmtpPassword\", smtpPassword)");

        var settings = files["src/BuildingBlocks/Email/SmtpSettings.cs"];
        foreach (var key in (string[])["\"mail\"", "\"Email:SenderAddress\"", "\"Email:AllowUnencryptedSmtp\"", "\"Email:SmtpUserName\"", "\"Email:SmtpPassword\""])
        {
            settings.ShouldContain(key);
        }

        files.Where(file => Path.GetFileName(file.Key).StartsWith("appsettings", StringComparison.Ordinal))
            .ShouldAllBe(file => !file.Value.Contains("Email", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_keycloak_mailpit_comes_with_smtp_alone_and_nothing_waits_for_keycloak()
    {
        Files(Profile, "backend.auth=generic-oidc")["src/AppHost/AppHost.cs"].ShouldNotContain("mailpit");

        var appHost = Files(Profile, "backend.auth=generic-oidc", Smtp)["src/AppHost/AppHost.cs"];

        appHost.ShouldContain("IResourceBuilder<ContainerResource>? mailpit = null;");
        appHost.ShouldContain("mailpit = builder.AddContainer(\"mailpit\", \"axllent/mailpit\", \"v1.33\")");
        appHost.ShouldNotContain("keycloak");
        appHost.ShouldContain(".WithEmail(mailpit, smtpUserName, smtpPassword)");
    }

    [Fact]
    public void With_services_every_service_host_sends_email_and_the_parameters_are_declared_once()
    {
        var files = Files("saas-services-vue", Smtp);
        var appHost = files["src/AppHost/AppHost.cs"];

        var loop = appHost.IndexOf("foreach (var (module, project) in builder.ServiceHosts())", StringComparison.Ordinal);
        loop.ShouldBeGreaterThan(0);
        appHost.IndexOf(".WithEmail(mailpit, smtpUserName, smtpPassword)", StringComparison.Ordinal).ShouldBeGreaterThan(loop);
        appHost.Split("builder.AddParameter(\"smtp-password\", secret: true)").Length.ShouldBe(2);
        files.Where(file => file.Key.StartsWith("src/Services/", StringComparison.Ordinal) && file.Key.EndsWith("/Host/Program.cs", StringComparison.Ordinal))
            .ShouldAllBe(file => file.Value.Contains("builder.Services.AddEmail(builder.Configuration);"));
        files["src/Edge/Program.cs"].ShouldNotContain("AddEmail");
    }

    [Fact]
    public void A_project_sending_email_builds_and_its_email_module_and_architecture_tests_pass()
    {
        using var project = new GeneratedProject(Profile, Smtp);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/InvoiceMailer.cs", ApplicationSendingThroughTheBuildingBlock);
        project.Build();

        foreach (var tests in (string[])["tests/Email.Tests/Email.Tests.csproj", "tests/Modules/Billing.Tests/Billing.Tests.csproj", ArchitectureTests])
        {
            var (exitCode, output) = project.Test(tests);
            exitCode.ShouldBe(0, output);
            output.ShouldContain("failed: 0");
        }
    }

    [Fact]
    public void A_module_naming_mimekit_fails_the_architecture_tests_naming_the_assembly_and_the_file()
    {
        using var project = new GeneratedProject(Profile, Smtp);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/DirectMail.cs", ApplicationNamingMimeKit);
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("OnlyTheEmailWiringNamesMailKit");
        output.ShouldContain("Shop.Modules.Billing.Application references MimeKit;");
        output.ShouldContain("src/Modules/Billing/Billing.Application/DirectMail.cs:1: names MimeKit");
    }

    [Fact]
    public void An_smtp_password_or_leave_to_send_unencrypted_in_a_settings_file_fails_the_architecture_tests_naming_the_file_and_the_setting()
    {
        using var project = new GeneratedProject(Profile, Smtp);
        project.Write("src/Host/appsettings.Development.json", """
            {
              "Email": {
                "SenderAddress": "noreply@example.com",
                "SmtpUserName": "portal",
                "SmtpPassword": "not-a-real-smtp-password",
                "AllowUnencryptedSmtp": "true"
              }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/Host/appsettings.Development.json: 'Email:SmtpPassword' holds the SMTP password");
        output.ShouldContain("src/Host/appsettings.Development.json: 'Email:AllowUnencryptedSmtp' lets the host send e-mail unencrypted");
        output.ShouldNotContain("'Email:SenderAddress'");
        output.ShouldNotContain("'Email:SmtpUserName'");
        output.ShouldNotContain("not-a-real-smtp-password");
    }

    [Fact]
    public void An_email_sent_through_the_sender_reaches_mailpit_and_a_server_without_tls_is_refused_against_the_apphost()
    {
        using var project = new GeneratedProject(Profile, Smtp);
        project.Build();

        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test",
            "--project",
            "tests/Integration.Tests/Integration.Tests.csproj",
            "-c",
            "Release",
            "--no-build",
            "--no-ansi",
            "--filter-class",
            "Shop.Integration.Tests.EmailTests");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 2");
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty(profile);
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}
