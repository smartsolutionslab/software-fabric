using System.Text.Json;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.api-protocols: [rest, grpc]</c>: a module serves gRPC beside REST. Its contract is a <c>.proto</c> file
/// in the module's Api project, compiled at build time; a service implements the generated base, translates each call
/// to a command or query, sends it through <c>IDispatcher</c> and holds no business logic. The host maps every module's
/// services on an HTTP/2 endpoint of its own, for signed-in callers unless <c>backend.auth: none</c>.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class GrpcTests
{
    private const string Profile = "lean-crud";

    private const string Grpc = "backend.api-protocols=rest,grpc";

    private const string GrpcServicesTests = "tests/GrpcServices.Tests/GrpcServices.Tests.csproj";

    private const string ArchitectureTests = "tests/Architecture.Tests/Architecture.Tests.csproj";

    private const string BillingProto = """
        syntax = "proto3";

        option csharp_namespace = "Shop.Modules.Billing.Api.GrpcServices";

        package billing;

        service Invoices {
          rpc Count (CountRequest) returns (CountReply);
        }

        message CountRequest {
          string customer = 1;
        }

        message CountReply {
          int32 count = 1;
        }
        """;

    private const string CountInvoices = """
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.CountInvoices;

        public sealed record CountInvoices(string Customer) : IQuery<Result<int, ApiError>>;

        public sealed class CountInvoicesHandler : IQueryHandler<CountInvoices, Result<int, ApiError>>
        {
            public Task<Result<int, ApiError>> HandleAsync(CountInvoices query, CancellationToken cancellationToken)
            {
                Ensure.That(query).IsNotNull();

                if (query.Customer.Length == 0)
                {
                    return Task.FromResult<Result<int, ApiError>>(Failure<ApiError>(new NoCustomer()));
                }

                return Task.FromResult<Result<int, ApiError>>(Success(query.Customer.Length));
            }
        }

        public sealed record NoCustomer() : ApiError("billing.no-customer", "Name a customer.", System.Net.HttpStatusCode.BadRequest);
        """;

    private const string InvoicesService = """
        using Grpc.Core;
        using Shop.BuildingBlocks.Messaging;
        using Shop.Modules.Billing.Application.Queries.CountInvoices;

        namespace Shop.Modules.Billing.Api.GrpcServices;

        public sealed class InvoicesService(IDispatcher dispatcher) : Invoices.InvoicesBase
        {
            public override async Task<CountReply> Count(CountRequest request, ServerCallContext context)
            {
                Ensure.That(request).IsNotNull();
                Ensure.That(context).IsNotNull();

                var result = await dispatcher.QueryAsync(new CountInvoices(request.Customer), context.CancellationToken);
                return result.ToGrpcReply(count => new CountReply { Count = count });
            }
        }
        """;

    private const string InvoicesServiceReachingPastTheDispatcher = """
        using Grpc.Core;
        using Shop.BuildingBlocks.Messaging;
        using Shop.Modules.Billing.Application.Queries.CountInvoices;

        namespace Shop.Modules.Billing.Api.GrpcServices;

        public sealed class InvoicesService(IDispatcher dispatcher, IServiceProvider services) : Invoices.InvoicesBase
        {
            public IServiceProvider Services { get; } = services;

            public override async Task<CountReply> Count(CountRequest request, ServerCallContext context)
            {
                Ensure.That(request).IsNotNull();
                Ensure.That(context).IsNotNull();

                var result = await dispatcher.QueryAsync(new CountInvoices(request.Customer), context.CancellationToken);
                return result.ToGrpcReply(count => new CountReply { Count = count });
            }
        }
        """;

    private const string BillingInProcessTests = """
        using Grpc.Core;
        using Shop.Modules.Billing.Api.GrpcServices;

        namespace Shop.GrpcServices.Tests;

        public sealed class BillingTests : IDisposable
        {
            private readonly GrpcHost host = new();

            [Fact]
            public async Task The_billing_modules_query_answers_over_grpc()
            {
                using var channel = host.CreateChannel();
                var client = new Invoices.InvoicesClient(channel);

                var reply = await client.CountAsync(new CountRequest { Customer = "acme" }, GrpcHost.SignedInAs("alice"), cancellationToken: TestContext.Current.CancellationToken);

                reply.Count.ShouldBe(4);
            }

            [Fact]
            public async Task The_billing_modules_failure_is_an_rpc_error_with_its_code()
            {
                using var channel = host.CreateChannel();
                var client = new Invoices.InvoicesClient(channel);

                var error = await Should.ThrowAsync<RpcException>(async () => await client.CountAsync(new CountRequest(), GrpcHost.SignedInAs("alice"), cancellationToken: TestContext.Current.CancellationToken));

                error.StatusCode.ShouldBe(StatusCode.InvalidArgument);
                error.Status.Detail.ShouldBe("Name a customer.");
                error.Trailers.GetValue("error-code").ShouldBe("billing.no-customer");
            }

            public void Dispose() => host.Dispose();
        }
        """;

    private const string BillingThroughAspireTests = """
        using Aspire.Hosting.Testing;
        using Grpc.Core;
        using Grpc.Net.Client;
        using Shop.Modules.Billing.Api.GrpcServices;

        namespace Shop.Integration.Tests;

        public sealed class BillingGrpcTests(AspireFixture aspire)
        {
            [Fact]
            public async Task The_billing_modules_query_answers_over_grpc_on_the_hosts_grpc_endpoint()
            {
                using var signedIn = await aspire.CreateHostClientAsync("developer");
                var headers = new Metadata { { "Authorization", signedIn.DefaultRequestHeaders.Authorization!.ToString() } };
                using var channel = GrpcChannel.ForAddress(aspire.App.GetEndpoint("host", "grpc"));
                var client = new Invoices.InvoicesClient(channel);

                var reply = await client.CountAsync(new CountRequest { Customer = "acme" }, headers, cancellationToken: TestContext.Current.CancellationToken);

                reply.Count.ShouldBe(4);
            }

            [Fact]
            public async Task An_anonymous_grpc_call_is_refused()
            {
                using var channel = GrpcChannel.ForAddress(aspire.App.GetEndpoint("host", "grpc"));
                var client = new Invoices.InvoicesClient(channel);

                var error = await Should.ThrowAsync<RpcException>(async () => await client.CountAsync(new CountRequest { Customer = "acme" }, cancellationToken: TestContext.Current.CancellationToken));

                error.StatusCode.ShouldBe(StatusCode.Unauthenticated);
            }
        }
        """;

    [Fact]
    public void A_project_serving_grpc_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [Grpc]);

        generated.Errors.ShouldBeEmpty();
        generated.Files.Where(file => file.Path.StartsWith("docs/adr/", StringComparison.Ordinal))
            .Select(file => file.Content)
            .ShouldAllBe(content => !content.Contains("This option's pack is not written yet.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_host_maps_the_modules_grpc_services_on_an_http2_endpoint_of_its_own()
    {
        var files = Files(Profile, Grpc);

        files["src/Host/Host.csproj"].ShouldContain("<PackageReference Include=\"Grpc.AspNetCore\" />");
        files["src/Host/Program.cs"].ShouldContain("builder.Services.AddModuleGrpcServices(modules);");
        files["src/Host/Program.cs"].ShouldContain("app.MapModuleGrpcServices();");
        files["src/Host/Wiring/GrpcServiceEndpoints.cs"].ShouldContain(".RequireAuthorization()");
        files["src/Modules/Directory.Build.props"].ShouldContain("<Protobuf Include=\"Protos/**/*.proto\" GrpcServices=\"Both\" />");
        files["src/BuildingBlocks/BuildingBlocks.csproj"].ShouldContain("<PackageReference Include=\"Grpc.Core.Api\" />");
        files["Directory.Packages.props"].ShouldContain("<PackageVersion Include=\"Grpc.AspNetCore\" Version=\"2.85.0\" />");
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/GrpcServicesOnlyDispatch.cs");
        files["Solution.slnx"].ShouldContain(GrpcServicesTests);

        using var settings = JsonDocument.Parse(files["src/Host/appsettings.json"]);
        var grpc = settings.RootElement.GetProperty("Kestrel").GetProperty("Endpoints").GetProperty("grpc");
        grpc.GetProperty("Url").GetString().ShouldBe("http://localhost:5081");
        grpc.GetProperty("Protocols").GetString().ShouldBe("Http2");
    }

    [Fact]
    public void Without_authentication_the_grpc_services_are_open()
    {
        var files = Files(Profile, Grpc, "backend.auth=none");

        files["src/Host/Wiring/GrpcServiceEndpoints.cs"].ShouldNotContain("RequireAuthorization");
    }

    [Fact]
    public void No_profile_references_grpc()
    {
        foreach (var profile in new[] { "lean-crud", "saas-standard-react", "saas-standard-vue", "industrial-event-driven-mfe" })
        {
            var files = Files(profile);
            files["Directory.Packages.props"].ShouldNotContain("Grpc", customMessage: profile);
            files["src/Host/appsettings.json"].ShouldNotContain("Kestrel", customMessage: profile);
        }
    }

    [Fact]
    public void A_modules_query_answers_over_grpc_in_process_and_the_architecture_rules_pass()
    {
        using var project = NewProject(Grpc);
        WriteBilling(project, InvoicesService);
        project.WriteTypes("tests/GrpcServices.Tests", BillingInProcessTests);
        project.Build();

        var (grpcExitCode, grpcOutput) = project.Test(GrpcServicesTests);
        grpcExitCode.ShouldBe(0, grpcOutput);
        grpcOutput.ShouldContain("failed: 0");
        grpcOutput.ShouldContain("succeeded: 5");

        var (architectureExitCode, architectureOutput) = project.Test(ArchitectureTests);
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_grpc_service_that_takes_more_than_the_dispatcher_fails_the_architecture_tests_naming_it()
    {
        using var project = NewProject(Grpc);
        WriteBilling(project, InvoicesServiceReachingPastTheDispatcher);
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Api.GrpcServices.InvoicesService takes System.IServiceProvider; a gRPC service takes only IDispatcher");
    }

    [Fact]
    public void Without_authentication_and_with_minimal_results_the_generated_grpc_tests_pass()
    {
        // The BCL's guards: minimal's own Result does not compile with Ensure.That's (on main too), which is not this pack's.
        using var project = NewProject(Grpc, "backend.auth=none", "backend.result-style=minimal", "backend.guards=bcl", "backend.absence=nullable");
        project.Build();

        var (exitCode, output) = project.Test(GrpcServicesTests);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 2");
    }

    [Fact]
    public void A_modules_query_answers_over_grpc_through_aspire_and_the_integration_tests_pass()
    {
        using var project = NewProject(Grpc);
        WriteBilling(project, InvoicesService);
        project.WriteTypes("tests/Integration.Tests", BillingThroughAspireTests);
        var testProject = Path.Combine(project.ProjectDirectory, "tests", "Integration.Tests", "Integration.Tests.csproj");
        project.Write("tests/Integration.Tests/Integration.Tests.csproj", File.ReadAllText(testProject).Replace(
            "<ProjectReference Include=\"../../src/AppHost/AppHost.csproj\" />",
            "<ProjectReference Include=\"../../src/AppHost/AppHost.csproj\" />\n    <ProjectReference Include=\"../../src/Modules/Billing/Billing.Api/Billing.Api.csproj\" />",
            StringComparison.Ordinal));
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    /// <summary>A billing module whose query answers over gRPC through the given service.</summary>
    private static void WriteBilling(GeneratedProject project, string service)
    {
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/Protos/billing.proto", BillingProto + "\n");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/CountInvoices", CountInvoices);
        project.WriteTypes("src/Modules/Billing/Billing.Api/GrpcServices", service);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>A lean-crud project with the overrides; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(params string[] overrides)
    {
        var project = new GeneratedProject(Profile, overrides);
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
