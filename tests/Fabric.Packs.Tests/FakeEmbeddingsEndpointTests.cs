using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.llm.extras: rag</c> in the application's integration tests (#633): the fixture starts
/// <c>FakeEmbeddingsServer</c>, which speaks the chosen embeddings provider's protocol on the loopback interface and
/// answers with <c>FakeEmbeddingGenerator</c>'s vectors, and points the host at it. A use case that embeds can then be
/// tested through the host, with no key and no real model; a test can hold an answer back. A project without rag has
/// no such server.
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

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}
