namespace Fabric.Packs.Tests;

/// <summary>backend.errors.result at the HTTP edge: a failed guard is the caller's fault, anything else is ours and says nothing.</summary>
[Collection(FixedPorts.Name)]
public sealed class ErrorHandlingTests
{
    private const string Amount = """
        namespace Shop.Modules.Billing.Domain.Invoices;

        public sealed record Amount
        {
            private Amount(decimal value) => Value = value;

            public decimal Value { get; }

            public static Amount From(decimal value)
            {
                Ensure.That(value).AtLeast(0m);
                return new Amount(value);
            }
        }
        """;

    private const string InvoicesEndpoint = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;
        using Shop.Modules.Billing.Domain.Invoices;

        namespace Shop.Modules.Billing.Api;

        public sealed record IssueInvoiceRequest(decimal Amount);

        public sealed class Invoices : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints)
            {
                endpoints.MapPost("/invoices", (IssueInvoiceRequest request) =>
                    Result<decimal, ApiError>.Success(Amount.From(request.Amount).Value).ToHttpResult());
                endpoints.MapPost("/invoices/broken", string () => throw new InvalidOperationException("connection string Password=hunter2"));
            }
        }
        """;

    private const string GuardTests = """
        using System.Net;
        using System.Net.Http.Json;
        using System.Text.Json;

        namespace Shop.Integration.Tests;

        public class GuardTests(AspireFixture aspire)
        {
            [Fact]
            public async Task A_value_its_value_object_rejects_answers_400_problem_details()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var client = await aspire.CreateHostClientAsync("developer");

                using var response = await client.PostAsJsonAsync(new Uri("/billing/invoices", UriKind.Relative), new { Amount = -1m }, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
                response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json", body);
                using var problem = JsonDocument.Parse(body);
                problem.RootElement.GetProperty("status").GetInt32().ShouldBe(400, body);
                problem.RootElement.GetProperty("code").GetString().ShouldBe("invalid-argument", body);
            }

            [Fact]
            public async Task A_valid_value_still_succeeds()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var client = await aspire.CreateHostClientAsync("developer");

                using var response = await client.PostAsJsonAsync(new Uri("/billing/invoices", UriKind.Relative), new { Amount = 12.5m }, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
            }

            [Fact]
            public async Task Any_other_exception_answers_500_problem_details_without_its_details()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var client = await aspire.CreateHostClientAsync("developer");

                using var response = await client.PostAsync(new Uri("/billing/invoices/broken", UriKind.Relative), null, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError, body);
                response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json", body);
                body.ShouldNotContain("hunter2");
                body.ShouldNotContain("InvalidOperationException");
            }
        }
        """;

    [Fact]
    public void A_failed_guard_answers_400_problem_details_and_any_other_exception_a_bare_500()
    {
        using var project = new GeneratedProject("saas-standard-react");
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/Amount.cs", Amount);
        project.Write("src/Modules/Billing/Billing.Api/Invoices.cs", InvoicesEndpoint);
        project.Write("tests/Integration.Tests/GuardTests.cs", GuardTests);
        project.Build();

        // Only these tests: the new endpoints make the frontend's OpenAPI copy stale, which another test reports.
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
            "Shop.Integration.Tests.GuardTests");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 3");
    }
}
