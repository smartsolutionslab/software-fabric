using System.Net;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.mediator.wolverine</c> (M3-23, #249): <c>IDispatcher</c> sends commands and queries through Wolverine's
/// bus, which the Wolverine messaging library already runs in the host, so the handlers run in Wolverine's pipeline.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class WolverineMediatorTests
{
    private const string Profile = "saas-standard-react";
    private const string Mediator = "backend.mediator=wolverine";
    private const string DatabasePassword = "fabric-tests";

    private const string Query = """
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.CountLetters;

        public sealed record CountLetters(string Text) : IQuery<Result<int, ApiError>>;

        public sealed class CountLettersHandler : IQueryHandler<CountLetters, Result<int, ApiError>>
        {
            public Task<Result<int, ApiError>> HandleAsync(CountLetters query, CancellationToken cancellationToken)
            {
                Ensure.That(query).IsNotNull();
                return Task.FromResult(Result<int, ApiError>.Success(query.Text.Length));
            }
        }
        """;

    private const string Command = """
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Commands.Shout;

        public sealed record Shout(string Text) : ICommand<Result<string, ApiError>>;

        public sealed class ShoutHandler : ICommandHandler<Shout, Result<string, ApiError>>
        {
            public Task<Result<string, ApiError>> HandleAsync(Shout command, CancellationToken cancellationToken)
            {
                Ensure.That(command).IsNotNull();
                return Task.FromResult(Result<string, ApiError>.Success(command.Text.ToUpperInvariant()));
            }
        }
        """;

    private const string Endpoints = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;
        using Shop.BuildingBlocks.Messaging;
        using Shop.Modules.Billing.Application.Commands.Shout;
        using Shop.Modules.Billing.Application.Queries.CountLetters;

        namespace Shop.Modules.Billing.Api;

        public sealed class Letters : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints)
            {
                endpoints.MapGet("/letters/{text}", async (string text, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                    (await dispatcher.QueryAsync(new CountLetters(text), cancellationToken)).ToHttpResult()).AllowAnonymous();
                endpoints.MapPost("/shouts/{text}", async (string text, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                    (await dispatcher.SendAsync(new Shout(text), cancellationToken)).ToHttpResult()).AllowAnonymous();
            }
        }
        """;

    [Fact]
    public async Task A_query_and_a_command_sent_through_the_dispatcher_are_handled_on_wolverines_bus()
    {
        using var project = new GeneratedProject(Profile, Mediator);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/CountLetters", Query);
        project.WriteTypes("src/Modules/Billing/Billing.Application/Commands/Shout", Command);
        project.Write("src/Modules/Billing/Billing.Api/Letters.cs", Endpoints);
        project.Build();

        var dispatcher = File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "BuildingBlocks", "Messaging", "Dispatcher.cs"));
        dispatcher.ShouldContain("IMessageBus");
        dispatcher.ShouldContain(".InvokeAsync<TResult>(");

        using var postgres = Container.Postgres(DatabasePassword);
        using var rabbitMq = Container.RabbitMq();
        using var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
            ["ConnectionStrings__messaging"] = $"amqp://guest:guest@127.0.0.1:{rabbitMq.Port}",
            ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
        });

        var counted = await host.GetAsync("/billing/letters/fabric");
        var countedBody = await counted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        counted.StatusCode.ShouldBe(HttpStatusCode.OK, countedBody);
        countedBody.ShouldBe("6");

        var shouted = await host.PostAsync("/billing/shouts/fabric");
        var shoutedBody = await shouted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        shouted.StatusCode.ShouldBe(HttpStatusCode.OK, shoutedBody);
        shoutedBody.ShouldBe("\"FABRIC\"");

        // Invoked inline: a command or a query never reaches the broker, so RabbitMQ has no queue named after one.
        var queues = RabbitMqQueues(rabbitMq);
        queues.Contains("CountLetters", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(queues);
        queues.Contains("Shout", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(queues);
    }

    /// <summary>
    /// A module's handler takes the module's own services, its unit of work and, in an event-sourced module, its aggregate
    /// store, which Wolverine's generated handler code cannot build (#644): the unit of work takes the service provider,
    /// the aggregate store is registered by a factory. A module added with <c>fabric add-module</c> names them nowhere,
    /// and its command saves through them all the same.
    /// </summary>
    [Theory]
    [InlineData("ddd")]
    [InlineData("event-sourced")]
    public async Task A_modules_command_whose_handler_takes_its_unit_of_work_and_aggregate_store_saves_through_them(string style)
    {
        using var project = new GeneratedProject(Profile, Mediator, "backend.tenancy=none", $"module.style={style}");
        project.AddModule("billing");
        project.Write("src/Contracts/Billing/NoteRecordedV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record NoteRecordedV1(string Text) : IntegrationEvent;
            """);
        var aggregateStore = style == "event-sourced" ? ", IBillingAggregateStore aggregates" : string.Empty;
        var aggregateStoreUsed = style == "event-sourced" ? "        Ensure.That(aggregates).IsNotNull();\n" : string.Empty;
        project.WriteTypes("src/Modules/Billing/Billing.Application/Commands/RecordNote", $$"""
            using Shop.BuildingBlocks.Messaging;
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Billing.Application.Commands.RecordNote;

            public sealed record RecordNote(string Text) : ICommand<Result<string, ApiError>>;

            public sealed class RecordNoteHandler(IBillingUnitOfWork unitOfWork{{aggregateStore}}) : ICommandHandler<RecordNote, Result<string, ApiError>>
            {
                public async Task<Result<string, ApiError>> HandleAsync(RecordNote command, CancellationToken cancellationToken)
                {
                    Ensure.That(command).IsNotNull();
            {{aggregateStoreUsed}}
                    await unitOfWork.PublishAsync(new NoteRecordedV1(command.Text));
                    await unitOfWork.CommitAsync(cancellationToken);
                    return Result<string, ApiError>.Success(command.Text);
                }
            }
            """);
        project.Write("src/Modules/Billing/Billing.Api/Notes.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.BuildingBlocks.Messaging;
            using Shop.Modules.Billing.Application.Commands.RecordNote;

            namespace Shop.Modules.Billing.Api;

            public sealed class Notes : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapPost("/notes/{text}", async (string text, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                        (await dispatcher.SendAsync(new RecordNote(text), cancellationToken)).ToHttpResult()).AllowAnonymous();
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var rabbitMq = Container.RabbitMq();
        using var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
            ["ConnectionStrings__messaging"] = $"amqp://guest:guest@127.0.0.1:{rabbitMq.Port}",
            ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
        });

        var recorded = await host.PostAsync("/billing/notes/fabric");
        var recordedBody = await recorded.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        recorded.StatusCode.ShouldBe(HttpStatusCode.OK, recordedBody);
        recordedBody.ShouldBe("\"fabric\"");
    }

    /// <summary>
    /// The test hosts of agents, dashboards, gRPC and the MCP server send requests through <c>IDispatcher</c>, so they
    /// start Wolverine in process (#362); before, they failed with Wolverine's bus missing or not started.
    /// </summary>
    [Fact]
    public void With_agents_dashboards_grpc_and_an_mcp_server_their_tests_send_requests_through_wolverine_and_pass()
    {
        using var project = new GeneratedProject("saas-ai-react", Mediator, "backend.api-protocols=rest,grpc", "backend.mcp=server");
        project.Build();

        foreach (var tests in new[]
        {
            "tests/Agents.Tests/Agents.Tests.csproj",
            "tests/Modules/Assistant.Tests/Assistant.Tests.csproj",
            "tests/Modules/Dashboards.Tests/Dashboards.Tests.csproj",
            "tests/GrpcServices.Tests/GrpcServices.Tests.csproj",
            "tests/Mcp.Server.Tests/Mcp.Server.Tests.csproj",
            "tests/Architecture.Tests/Architecture.Tests.csproj",
        })
        {
            var (exitCode, output) = project.Test(tests);
            exitCode.ShouldBe(0, $"{tests}\n{output}");
            output.ShouldContain("failed: 0", customMessage: tests);
        }
    }

    private static string RabbitMqQueues(Container rabbitMq)
    {
        var start = new System.Diagnostics.ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "exec", rabbitMq.Id, "rabbitmqctl", "list_queues", "name" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, output);
        return output;
    }

    [Fact]
    public void The_module_and_architecture_tests_pass_and_a_request_without_a_handler_fails_them()
    {
        using var project = new GeneratedProject(Profile, Mediator);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/CountLetters", Query);
        project.WriteTypes("src/Modules/Billing/Billing.Application/Commands/Shout", Command);
        project.Build();

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "AGENTS.md")).ShouldContain("## Mediator: Wolverine");
        File.ReadAllText(Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "docs", "adr"), "*-backend-mediator-wolverine.md").Single())
            .ShouldContain("# Mediator: Wolverine");

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");

        project.Write("src/Modules/Billing/Billing.Application/Commands/Whisper/Whisper.cs", """
            using Shop.BuildingBlocks.Messaging;

            namespace Shop.Modules.Billing.Application.Commands.Whisper;

            public sealed record Whisper(string Text) : ICommand<Result<string, ApiError>>;
            """);
        project.Build();

        var (unhandledExitCode, unhandledOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        unhandledExitCode.ShouldNotBe(0, unhandledOutput);
        unhandledOutput.ShouldContain("Shop.Modules.Billing.Application.Commands.Whisper.Whisper has 0 handlers.");
    }

    /// <summary>Wolverine invokes only public handler types through public, conventionally named methods.</summary>
    [Fact]
    public void A_handler_wolverine_cannot_invoke_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Mediator);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/CountLetters", Query.Replace(
            "public sealed class CountLettersHandler", "internal sealed class CountLettersHandler", StringComparison.Ordinal));
        project.WriteTypes("src/Modules/Billing/Billing.Application/Commands/Shout", Command.Replace(
            "public Task<Result<string, ApiError>> HandleAsync(", "Task<Result<string, ApiError>> ICommandHandler<Shout, Result<string, ApiError>>.HandleAsync(", StringComparison.Ordinal));
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application.Queries.CountLetters.CountLettersHandler is not public; Wolverine invokes only public handlers.");
        output.ShouldContain("Shop.Modules.Billing.Application.Commands.Shout.ShoutHandler has no public HandleAsync for its ICommandHandler of Shop.Modules.Billing.Application.Commands.Shout.Shout; Wolverine does not invoke an explicit interface implementation.");
    }

    /// <summary>A public HandleAsync for one contract does not make a class's explicit implementation of another invokable.</summary>
    [Fact]
    public void A_handler_contract_implemented_only_explicitly_fails_the_architecture_tests_beside_a_public_one()
    {
        using var project = new GeneratedProject(Profile, Mediator);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/Queries/Peek/Peek.cs", """
            using Shop.BuildingBlocks.Messaging;

            namespace Shop.Modules.Billing.Application.Queries.Peek;

            public sealed record Peek(string Text) : IQuery<Result<int, ApiError>>;
            """);
        project.WriteTypes("src/Modules/Billing/Billing.Application/Commands/Stamp", """
            using Shop.BuildingBlocks.Messaging;
            using Shop.Modules.Billing.Application.Queries.Peek;

            namespace Shop.Modules.Billing.Application.Commands.Stamp;

            public sealed record Stamp(string Text) : ICommand<Result<int, ApiError>>;

            public sealed class LettersHandler : ICommandHandler<Stamp, Result<int, ApiError>>, IQueryHandler<Peek, Result<int, ApiError>>
            {
                public Task<Result<int, ApiError>> HandleAsync(Stamp command, CancellationToken cancellationToken)
                {
                    Ensure.That(command).IsNotNull();
                    return Task.FromResult(Result<int, ApiError>.Success(command.Text.Length));
                }

                Task<Result<int, ApiError>> IQueryHandler<Peek, Result<int, ApiError>>.HandleAsync(Peek query, CancellationToken cancellationToken)
                {
                    Ensure.That(query).IsNotNull();
                    return Task.FromResult(Result<int, ApiError>.Success(query.Text.Length));
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application.Commands.Stamp.LettersHandler has no public HandleAsync for its IQueryHandler of Shop.Modules.Billing.Application.Queries.Peek.Peek; Wolverine does not invoke an explicit interface implementation.");
        output.ShouldNotContain("Shop.Modules.Billing.Application.Commands.Stamp.LettersHandler has no public HandleAsync for its ICommandHandler");
    }

    /// <summary>Wolverine discovers, and the module registers, only concrete classes, so a struct or an abstract class is no handler.</summary>
    [Fact]
    public void A_struct_or_an_abstract_class_implementing_a_handler_contract_is_no_handler_and_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Mediator);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/CountLetters", Query.Replace(
            "public sealed class CountLettersHandler", "public readonly record struct CountLettersHandler", StringComparison.Ordinal));
        project.WriteTypes("src/Modules/Billing/Billing.Application/Commands/Shout", Command.Replace(
            "public sealed class ShoutHandler", "public abstract class ShoutHandler", StringComparison.Ordinal));
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application.Queries.CountLetters.CountLetters has 0 handlers.");
        output.ShouldContain("Shop.Modules.Billing.Application.Queries.CountLetters.CountLettersHandler implements a handler contract of Shop.Modules.Billing.Application.Queries.CountLetters.CountLetters but is not a concrete class; Wolverine invokes only concrete classes.");
        output.ShouldContain("Shop.Modules.Billing.Application.Commands.Shout.Shout has 0 handlers.");
        output.ShouldContain("Shop.Modules.Billing.Application.Commands.Shout.ShoutHandler implements a handler contract of Shop.Modules.Billing.Application.Commands.Shout.Shout but is not a concrete class; Wolverine invokes only concrete classes.");
    }

    /// <summary>An abstract base that implements the contract, with one concrete class deriving from it, is one handler.</summary>
    [Fact]
    public void An_abstract_base_handler_with_one_concrete_derived_class_counts_as_one_handler()
    {
        using var project = new GeneratedProject(Profile, Mediator);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/CountLetters", Query.Replace(
            "public sealed class CountLettersHandler", "public abstract class LetterCounter", StringComparison.Ordinal) + """

            public sealed class CountLettersHandler : LetterCounter;
            """);
        project.WriteTypes("src/Modules/Billing/Billing.Application/Commands/Shout", Command);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    /// <summary>Wolverine invokes the public HandleAsync for the request, so that method must be the contract's, with its full signature.</summary>
    [Fact]
    public void A_public_handle_async_that_does_not_implement_the_handler_contract_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Mediator);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/CountLetters", Query);
        project.WriteTypes("src/Modules/Billing/Billing.Application/Commands/Shout", """
            using Shop.BuildingBlocks.Messaging;

            namespace Shop.Modules.Billing.Application.Commands.Shout;

            public sealed record Shout(string Text) : ICommand<Result<string, ApiError>>;

            public sealed class ShoutHandler : ICommandHandler<Shout, Result<string, ApiError>>
            {
                private int shouts;

                public Task<Result<int, ApiError>> HandleAsync(Shout command, CancellationToken cancellationToken)
                {
                    Ensure.That(command).IsNotNull();
                    return Task.FromResult(Result<int, ApiError>.Success(++shouts));
                }

                Task<Result<string, ApiError>> ICommandHandler<Shout, Result<string, ApiError>>.HandleAsync(Shout command, CancellationToken cancellationToken)
                {
                    Ensure.That(command).IsNotNull();
                    return Task.FromResult(Result<string, ApiError>.Success(command.Text.ToUpperInvariant()));
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application.Commands.Shout.ShoutHandler has a public Task<Result<Int32, ApiError>> HandleAsync(Shout, CancellationToken) that does not implement its ICommandHandler of Shop.Modules.Billing.Application.Commands.Shout.Shout; Wolverine would invoke it, and the contract is Task<Result<String, ApiError>> HandleAsync(Shout, CancellationToken).");
        output.ShouldNotContain("CountLettersHandler");
    }

    /// <summary>Wolverine also invokes a public static HandleAsync, so one taking the request beside the contract's method fails too.</summary>
    [Fact]
    public void A_public_static_handle_async_beside_the_contracts_method_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Mediator);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/CountLetters", Query);
        project.WriteTypes("src/Modules/Billing/Billing.Application/Commands/Shout", Command.Replace(
            "    public Task<Result<string, ApiError>> HandleAsync(", """
                public static Task<Result<int, ApiError>> HandleAsync(Shout command)
                {
                    Ensure.That(command).IsNotNull();
                    return Task.FromResult(Result<int, ApiError>.Success(command.Text.Length));
                }

                public Task<Result<string, ApiError>> HandleAsync(
            """, StringComparison.Ordinal));
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application.Commands.Shout.ShoutHandler has a public Task<Result<Int32, ApiError>> HandleAsync(Shout) that does not implement its ICommandHandler of Shop.Modules.Billing.Application.Commands.Shout.Shout; Wolverine would invoke it, and the contract is Task<Result<String, ApiError>> HandleAsync(Shout, CancellationToken).");
        output.ShouldNotContain("CountLettersHandler");
    }
}
