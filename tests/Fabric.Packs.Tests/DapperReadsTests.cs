using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.data-access.reads: dapper</c> (owner decisions on #247): EF Core keeps the model, the writes and the
/// migrations; query handlers read with Dapper over the building blocks' <c>IReadConnection</c>, which opens connections
/// of the project's one database through the driver its database pack names in the slot
/// <c>read-connection.data-source</c>. The gate <c>DapperReadsOnlyInQueryHandlers</c> fails a module whose other
/// projects reference Dapper, or whose command handler takes the read connection. The generated
/// <c>tests/Reads.Tests</c> read a table through a sample query handler against the AppHost's database, in SQL both
/// databases accept.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class DapperReadsTests
{
    private const string Profile = "lean-crud";
    private const string Dapper = "backend.data-access.reads=dapper";
    private const string SqlServer = "backend.database=sqlserver";

    /// <summary>A billing query handler reading with Dapper, which the gate lets through and the module's container resolves.</summary>
    private const string BillingQueryReadingWithDapper = """
        using Dapper;
        using Shop.BuildingBlocks.Messaging;
        using Shop.BuildingBlocks.Persistence;

        namespace Shop.Modules.Billing.Application;

        public sealed record InvoiceCount : IQuery<Result<int, ApiError>>;

        public sealed class InvoiceCountHandler(IReadConnection reads) : IQueryHandler<InvoiceCount, Result<int, ApiError>>
        {
            public async Task<Result<int, ApiError>> HandleAsync(InvoiceCount query, CancellationToken cancellationToken)
            {
                await using var connection = await reads.OpenAsync(cancellationToken);
                return Success(await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*) FROM billing.invoices", cancellationToken: cancellationToken)));
            }
        }
        """;

    /// <summary>A billing command handler taking the read connection, which the gate refuses.</summary>
    private const string BillingCommandTakingTheReadConnection = """
        using Shop.BuildingBlocks.Messaging;
        using Shop.BuildingBlocks.Persistence;

        namespace Shop.Modules.Billing.Application;

        public sealed record CloseInvoices : ICommand<Result<int, ApiError>>;

        public sealed class CloseInvoicesHandler(IReadConnection reads) : ICommandHandler<CloseInvoices, Result<int, ApiError>>
        {
            public async Task<Result<int, ApiError>> HandleAsync(CloseInvoices command, CancellationToken cancellationToken)
            {
                await using var connection = await reads.OpenAsync(cancellationToken);
                return Success(0);
            }
        }
        """;

    /// <summary>Dapper in the billing module's Infrastructure project, which the gate refuses.</summary>
    private const string InfrastructureUsingDapper = """
        using Dapper;

        namespace Shop.Modules.Billing.Infrastructure;

        public static class InvoiceTotals
        {
            public static Task<int> CountAsync(System.Data.Common.DbConnection connection) =>
                connection.ExecuteScalarAsync<int>("SELECT count(*) FROM billing.invoices");
        }
        """;

    public static TheoryData<string, string> Drivers { get; } = new()
    {
        { "postgres", "Npgsql.NpgsqlDataSource.Create(connectionString)" },
        { "sqlserver", "Microsoft.Data.SqlClient.SqlClientFactory.Instance.CreateDataSource(connectionString)" },
    };

    [Theory]
    [InlineData("postgres")]
    [InlineData("sqlserver")]
    public void A_project_with_dapper_reads_generates_from_the_real_packs_alone(string database)
    {
        using var standIns = new StandIns();

        standIns.Complete(Repository.Catalog, Repository.Packs.Packs, Profile, Overrides(database));

        standIns.StoodIn.ShouldBeEmpty(database);
    }

    [Theory]
    [MemberData(nameof(Drivers))]
    public void Application_projects_get_dapper_and_the_read_connection_opens_through_the_databases_driver(string database, string driver)
    {
        var files = Files(Overrides(database));

        files["Directory.Packages.props"].ShouldContain("<PackageVersion Include=\"Dapper\" Version=\"2.1.89\" />");
        var modules = files["src/Modules/Directory.Build.props"];
        var application = modules[modules.IndexOf("'$(ModuleLayer)' == 'Application'", StringComparison.Ordinal)..modules.IndexOf("'$(ModuleLayer)' == 'Infrastructure'", StringComparison.Ordinal)];
        application.ShouldContain("<PackageReference Include=\"Dapper\" />");
        modules.Replace(application, string.Empty, StringComparison.Ordinal).ShouldNotContain("Dapper");

        files["src/BuildingBlocks/Persistence/IReadConnection.cs"].ShouldContain("ValueTask<DbConnection> OpenAsync(CancellationToken cancellationToken);");
        files["src/BuildingBlocks/Persistence/Wiring/ReadConnectionRegistration.cs"].ShouldContain($"new ReadConnection({driver})");
        files["src/BuildingBlocks/Modules/ModuleInfrastructure.cs"].ShouldContain("        services.AddReadConnection(configuration);\n");
        files["Solution.slnx"].ShouldContain("<Project Path=\"tests/Reads.Tests/Reads.Tests.csproj\" />");
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/DapperReadsOnlyInQueryHandlers.cs");
        files.Keys.ShouldNotContain("tests/Architecture.Tests/Rules/NoDapper.cs");
        files["fabric.yaml"].ShouldContain("  backend.data-access.reads: dapper\n");
    }

    [Fact]
    public void The_agents_are_told_to_write_sql_for_the_one_database_and_to_read_only_in_query_handlers()
    {
        var agents = Files(Overrides("postgres"))["AGENTS.md"];

        agents.ShouldContain("## Reads: Dapper");
        agents.ShouldContain("IReadConnection");
        agents.ShouldContain("PostgreSQL");
        Files(Overrides("sqlserver"))["AGENTS.md"].ShouldContain("SQL Server");
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("sqlserver")]
    public void The_generated_read_tests_read_through_dapper_in_the_aspire_database_and_a_query_handler_reading_with_it_passes_the_rules(string database)
    {
        using var project = NewProject(database);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application", BillingQueryReadingWithDapper);
        project.Build();

        var (readsExitCode, readsOutput) = project.Test("tests/Reads.Tests/Reads.Tests.csproj");
        readsExitCode.ShouldBe(0, readsOutput);
        readsOutput.ShouldContain("failed: 0");
        readsOutput.ShouldContain("succeeded: 2");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_command_handler_taking_the_read_connection_and_dapper_outside_application_fail_the_architecture_tests()
    {
        using var project = NewProject("postgres");
        project.AddModule("billing");
        var infrastructure = Path.Combine(project.ProjectDirectory, "src/Modules/Billing/Billing.Infrastructure/Billing.Infrastructure.csproj");
        File.WriteAllText(infrastructure, File.ReadAllText(infrastructure).Replace(
            "</Project>",
            "  <ItemGroup>\n    <PackageReference Include=\"Dapper\" />\n  </ItemGroup>\n\n</Project>",
            StringComparison.Ordinal));
        project.WriteTypes("src/Modules/Billing/Billing.Application", BillingCommandTakingTheReadConnection);
        project.WriteTypes("src/Modules/Billing/Billing.Infrastructure", InfrastructureUsingDapper);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Infrastructure references Dapper; only a module's Application project reads with it, in its query handlers.");
        output.ShouldContain("Shop.Modules.Billing.Application.CloseInvoicesHandler handles a command and takes IReadConnection; a command changes state through EF Core, only query handlers read with Dapper.");
    }

    private static string[] Overrides(string database) =>
        database == "postgres" ? [Dapper] : [Dapper, SqlServer];

    private static Dictionary<string, string> Files(string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>A lean-crud project with dapper reads; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(string database)
    {
        var project = new GeneratedProject(Profile, Overrides(database));
        var standIns = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "docs", "adr"))
            .Where(file => File.ReadAllText(file).Contains("This option's pack is not written yet.", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        if (standIns.Count > 0)
        {
            project.Dispose();
        }

        standIns.ShouldBeEmpty();
        return project;
    }
}
