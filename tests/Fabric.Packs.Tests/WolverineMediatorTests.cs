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
}
