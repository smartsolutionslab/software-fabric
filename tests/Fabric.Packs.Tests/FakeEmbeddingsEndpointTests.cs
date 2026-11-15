using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.llm.extras: rag</c> in the application's integration tests (#633): the fixture starts
/// <c>FakeEmbeddingsServer</c>, which speaks the chosen embeddings provider's protocol on the loopback interface and
/// answers with <c>FakeEmbeddingGenerator</c>'s vectors, and points the host at it. A use case that embeds can then be
/// tested through the host, with no key and no real model; a test can hold an answer back. Where the chat model shares
/// the embeddings' endpoint (Azure OpenAI, Ollama), the same server answers chat with the <c>FakeChatClient</c> a test
/// registers for a word (#662), so a project fakes both through one endpoint. A project without rag has no such server.
/// </summary>
[Collection(DockerPorts.Name)]
public sealed class FakeEmbeddingsEndpointTests
{
    private const string Profile = "lean-crud";
    private const string Rag = "backend.llm.extras=rag";
    private const string Server = "tests/Integration.Tests/FakeEmbeddingsServer.cs";
    private const string Testcontainers = "backend.test.integration=testcontainers";

    /// <summary>A module's endpoints indexing and searching its notes, as a use case that embeds would; out of the OpenAPI document.</summary>
    private const string NotesEndpoints = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;
        using Shop.BuildingBlocks.Rag;
        using Shop.Modules.Billing.Infrastructure.Persistence;

        namespace Shop.Modules.Billing.Api;

        public sealed class NotesEndpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints)
            {
                endpoints.MapPut("/notes/{key}", async (string key, NoteText note, ModuleVectorIndex<BillingDbContext> notes, CancellationToken cancellationToken) =>
                    {
                        await notes.IndexAsync(key, note.Text, cancellationToken);
                        return TypedResults.NoContent();
                    })
                    .AllowAnonymous()
                    .ExcludeFromDescription();
                endpoints.MapGet("/notes/nearest", async (string question, ModuleVectorIndex<BillingDbContext> notes, CancellationToken cancellationToken) =>
                        TypedResults.Ok((await notes.SearchAsync(question, 1, cancellationToken))[0]))
                    .AllowAnonymous()
                    .ExcludeFromDescription();
            }
        }

        public sealed record NoteText(string Text);
        """;

    /// <summary>A module's endpoint asking the project's chat model a question, plainly and streamed, as a use case would.</summary>
    private const string AskEndpoints = """
        using System.Text;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.AI;
        using Shop.BuildingBlocks.Api;

        namespace Shop.Modules.Billing.Api;

        public sealed class AskEndpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints)
            {
                endpoints.MapGet("/notes/ask", async (string question, IChatClient model, CancellationToken cancellationToken) =>
                        TypedResults.Ok((await model.GetResponseAsync(question, cancellationToken: cancellationToken)).Text))
                    .AllowAnonymous()
                    .ExcludeFromDescription();
                endpoints.MapGet("/notes/ask-streamed", async (string question, IChatClient model, CancellationToken cancellationToken) =>
                    {
                        var answer = new StringBuilder();
                        await foreach (var update in model.GetStreamingResponseAsync(question, cancellationToken: cancellationToken))
                        {
                            answer.Append(update.Text);
                        }

                        return TypedResults.Ok(answer.ToString());
                    })
                    .AllowAnonymous()
                    .ExcludeFromDescription();
            }
        }
        """;

    /// <summary>The chat providers that share their endpoint with the embeddings, under both integration-test styles.</summary>
    public static TheoryData<string, string> SharedEndpoints { get; } = new()
    {
        { "azure-openai", "backend.test.integration=aspire-fixture" },
        { "ollama", "backend.test.integration=aspire-fixture" },
        { "azure-openai", Testcontainers },
        { "ollama", Testcontainers },
    };

    public static TheoryData<string, string> SeparateEndpoints { get; } = new()
    {
        { "openai", "openai" },
        { "anthropic", "ollama" },
        { "anthropic", "azure-openai" },
        { "ollama", "openai" },
    };

    public static TheoryData<string, string, string> Providers { get; } = new()
    {
        { "openai", "openai", "backend.test.integration=aspire-fixture" },
        { "azure-openai", "azure-openai", "backend.test.integration=aspire-fixture" },
        { "anthropic", "ollama", "backend.test.integration=aspire-fixture" },
        { "openai", "openai", Testcontainers },
        { "anthropic", "ollama", Testcontainers },
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public void A_rag_projects_fixture_starts_the_fake_embeddings_server_and_points_the_host_at_it(string llm, string embeddings, string integration)
    {
        var files = Files(Profile, $"backend.llm={llm}", Rag, $"backend.llm.embeddings={embeddings}", integration);

        files.Keys.ShouldContain(Server);
        var fixture = integration == Testcontainers ? files["tests/Integration.Tests/ContainerFixture.cs"] : files["tests/Integration.Tests/AspireFixture.cs"];
        fixture.ShouldContain("private readonly FakeEmbeddingsServer embeddings = new();");
        fixture.ShouldContain("public FakeEmbeddingsServer Embeddings => embeddings;");
        fixture.ShouldContain("await embeddings.DisposeAsync();");
        fixture.ShouldContain("embeddings.Endpoint");
        fixture.ShouldNotContain("not-a-real-resource", Case.Sensitive, "the embeddings endpoint is the stand-in");
        fixture.ShouldNotContain("http://localhost:9", Case.Sensitive, "the embeddings endpoint is the stand-in");
        files["tests/Integration.Tests/Integration.Tests.csproj"].ShouldContain("<Compile Include=\"../Fakes/FakeEmbeddingGenerator.cs\" Link=\"Fakes/FakeEmbeddingGenerator.cs\" />");
    }

    [Theory]
    [MemberData(nameof(SharedEndpoints))]
    public void Where_chat_shares_the_embeddings_endpoint_a_test_can_give_the_fake_server_a_chat_answer(string provider, string integration)
    {
        var files = Files(Profile, $"backend.llm={provider}", Rag, $"backend.llm.embeddings={provider}", integration);

        files[Server].ShouldContain("public IDisposable Answer(string word, FakeChatClient model)");
        files["tests/Integration.Tests/Integration.Tests.csproj"].ShouldContain("<Compile Include=\"../Fakes/FakeChatClient.cs\" Link=\"Fakes/FakeChatClient.cs\" />");
    }

    [Theory]
    [MemberData(nameof(SeparateEndpoints))]
    public void Where_chat_has_its_own_endpoint_the_fake_server_answers_embeddings_only(string llm, string embeddings)
    {
        var files = Files(Profile, $"backend.llm={llm}", Rag, $"backend.llm.embeddings={embeddings}");

        files[Server].ShouldNotContain("FakeChatClient");
        files["tests/Integration.Tests/Integration.Tests.csproj"].ShouldNotContain("FakeChatClient");
    }

    /// <summary>
    /// With chat and embeddings on one endpoint, a test registers a chat answer on the fixture's fake server and the host
    /// gets it, asked plainly or streamed, while it indexes and searches through the same server; a chat request no test
    /// answers fails, as a model the host cannot reach.
    /// </summary>
    [Theory]
    [MemberData(nameof(SharedEndpoints))]
    public void A_chat_answer_the_test_gives_the_fake_server_reaches_the_host_beside_its_embeddings(string provider, string integration)
    {
        using var project = new GeneratedProject(Profile, $"backend.llm={provider}", Rag, $"backend.llm.embeddings={provider}", integration);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Api", NotesEndpoints);
        project.WriteTypes("src/Modules/Billing/Billing.Api", AskEndpoints);
        var fixture = integration == Testcontainers ? "ContainerFixture" : "AspireFixture";
        project.Write("tests/Integration.Tests/NotesTests.cs", NotesTests(fixture));
        project.Write("tests/Integration.Tests/AskTests.cs", AskTests(fixture));
        project.Build();
        DockerPorts.EnsureRunsAlone();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_profile_without_rag_has_no_fake_embeddings_server()
    {
        foreach (var profile in Repository.ProfilesNotChoosing("backend.llm.extras", "rag"))
        {
            var files = Files(profile);

            files.Keys.ShouldNotContain(Server, profile);
            files.Values.ShouldAllBe(content => !content.Contains("FakeEmbeddingsServer", StringComparison.Ordinal), profile);
        }
    }

    /// <summary>
    /// The generated integration tests, and one that indexes notes through the host and finds the nearest by its words,
    /// pass with the fixture's fake embeddings: the host embeds through the provider's own client, against the stand-in.
    /// An answer the test holds back keeps the host's request waiting until it is released.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void A_note_indexed_through_the_host_is_found_with_the_fixtures_fake_embeddings(string llm, string embeddings, string integration)
    {
        using var project = new GeneratedProject(Profile, $"backend.llm={llm}", Rag, $"backend.llm.embeddings={embeddings}", integration);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Api", NotesEndpoints);
        project.Write("tests/Integration.Tests/NotesTests.cs", NotesTests(integration == Testcontainers ? "ContainerFixture" : "AspireFixture"));
        project.Build();
        DockerPorts.EnsureRunsAlone();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    private static string NotesTests(string fixture) => $$"""
        using System.Net;
        using System.Net.Http.Json;

        namespace Shop.Integration.Tests;

        public class NotesTests({{fixture}} application)
        {
            [Fact]
            public async Task A_note_indexed_through_the_host_is_the_nearest_to_a_question_sharing_its_words()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var client = application.CreateHostClient();

                using var rent = await client.PutAsJsonAsync(new Uri("/billing/notes/rent", UriKind.Relative), new Note("the rent for march is unpaid"), cancellationToken);
                using var lunch = await client.PutAsJsonAsync(new Uri("/billing/notes/lunch", UriKind.Relative), new Note("a team lunch on friday"), cancellationToken);
                var nearest = await client.GetFromJsonAsync<Passage>(new Uri("/billing/notes/nearest?question=unpaid%20rent", UriKind.Relative), cancellationToken);

                rent.StatusCode.ShouldBe(HttpStatusCode.NoContent, await rent.Content.ReadAsStringAsync(cancellationToken));
                lunch.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                nearest.ShouldNotBeNull().Key.ShouldBe("rent");
            }

            [Fact]
            public async Task A_held_answer_keeps_the_host_waiting_until_it_is_released()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var client = application.CreateHostClient();

                Task<HttpResponseMessage> indexing;
                using (var held = application.Embeddings.Hold("invoice"))
                {
                    indexing = client.PutAsJsonAsync(new Uri("/billing/notes/invoice", UriKind.Relative), new Note("an invoice to send"), cancellationToken);
                    await held.Arrived.WaitAsync(cancellationToken);

                    held.Waiting.ShouldBe(1);
                    indexing.IsCompleted.ShouldBeFalse();
                }

                using var indexed = await indexing;
                indexed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            private sealed record Note(string Text);

            private sealed record Passage(string Key, string Text, double Distance);
        }
        """;

    private static string AskTests(string fixture) => $$"""
        using System.Net;
        using System.Net.Http.Json;
        using Shop.Tests.Fakes;

        namespace Shop.Integration.Tests;

        public class AskTests({{fixture}} application)
        {
            [Fact]
            public async Task The_host_gets_the_chat_answer_the_test_gave_and_embeds_through_the_same_server()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var client = application.CreateHostClient();
                using var model = new FakeChatClient("pay it by friday");
                using var answering = application.Embeddings.Answer("overdue", model);

                using var indexed = await client.PutAsJsonAsync(new Uri("/billing/notes/overdue", UriKind.Relative), new Note("the overdue rent"), cancellationToken);
                var answer = await client.GetFromJsonAsync<string>(new Uri("/billing/notes/ask?question=what%20about%20the%20overdue%20rent", UriKind.Relative), cancellationToken);
                var streamed = await client.GetFromJsonAsync<string>(new Uri("/billing/notes/ask-streamed?question=and%20the%20overdue%20rent%20now", UriKind.Relative), cancellationToken);

                indexed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await indexed.Content.ReadAsStringAsync(cancellationToken));
                answer.ShouldBe("pay it by friday");
                streamed.ShouldBe("pay it by friday");
                model.Requests.Count.ShouldBe(2);
                model.Requests[0].ShouldContain(message => message.Text == "what about the overdue rent");
                model.Requests[1].ShouldContain(message => message.Text == "and the overdue rent now");
            }

            [Fact]
            public async Task A_chat_request_no_test_answers_fails_in_the_host()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var client = application.CreateHostClient();

                using var unanswered = await client.GetAsync(new Uri("/billing/notes/ask?question=nobody%20answers%20this", UriKind.Relative), cancellationToken);

                unanswered.IsSuccessStatusCode.ShouldBeFalse();
            }

            private sealed record Note(string Text);
        }
        """;

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}
