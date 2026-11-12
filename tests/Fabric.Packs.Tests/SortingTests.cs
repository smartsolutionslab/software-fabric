namespace Fabric.Packs.Tests;

/// <summary>
/// Sorting: a list endpoint that sorts takes <c>sortBy</c> and <c>sortDirection</c> from the query, beside
/// <c>page</c> and <c>pageSize</c>, and the OpenAPI document names their values, so the generated client has them
/// (#617). The Kernel's <c>SortDirection</c> is named already; a query's sort field is named by its module.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class SortingTests
{
    private const string InvoiceSortField = """
        using System.Text.Json.Serialization;

        namespace Shop.Modules.Billing.Application.Queries.InvoiceNumbers;

        [JsonConverter(typeof(JsonStringEnumConverter<InvoiceSortField>))]
        public enum InvoiceSortField
        {
            Number,
            IssuedOn,
        }
        """;

    private const string DraftSortField = """
        namespace Shop.Modules.Billing.Application.Queries.InvoiceNumbers;

        public enum DraftSortField
        {
            Number,
        }
        """;

    private const string ListInvoiceNumbers = """
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.InvoiceNumbers;

        public sealed record ListInvoiceNumbers(Page Page, PageSize PageSize, Sorting<InvoiceSortField> Sorting) : IQuery<PagedResult<string>>;

        public sealed class ListInvoiceNumbersHandler : IQueryHandler<ListInvoiceNumbers, PagedResult<string>>
        {
            private static readonly string[] All = [.. Enumerable.Range(1, 25).Select(number => $"INV-{number:D4}")];

            public Task<PagedResult<string>> HandleAsync(ListInvoiceNumbers query, CancellationToken cancellationToken)
            {
                var (page, pageSize, sorting) = query;
                var sorted = sorting.Direction == SortDirection.Ascending ? All.Order(StringComparer.Ordinal) : All.OrderDescending(StringComparer.Ordinal);
                string[] items = [.. sorted.Skip(page.Skip(pageSize)).Take(pageSize.Value)];
                return Task.FromResult(new PagedResult<string>(items, All.Length, page.Value, pageSize.Value));
            }
        }
        """;

    private const string InvoiceNumberEndpoints = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;
        using Shop.BuildingBlocks.Messaging;
        using Shop.Modules.Billing.Application.Queries.InvoiceNumbers;

        namespace Shop.Modules.Billing.Api;

        public sealed class InvoiceNumberEndpoints : IEndpoint
        {
            private static readonly string[] FirstNumbers = ["INV-0001", "INV-0002"];

            public void Map(IEndpointRouteBuilder endpoints)
            {
                endpoints.MapGet("/invoice-numbers", async (Page? page, PageSize? pageSize, InvoiceSortField? sortBy, SortDirection? sortDirection, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await dispatcher.QueryAsync(
                        new ListInvoiceNumbers(page ?? Page.First, pageSize ?? PageSize.Default, new Sorting<InvoiceSortField>(sortBy ?? InvoiceSortField.Number, sortDirection ?? SortDirection.Ascending)),
                        cancellationToken)))
                    .AllowAnonymous();
                endpoints.MapGet("/drafts", (Page? page, PageSize? pageSize, DraftSortField? sortBy, SortDirection? sortDirection) =>
                    TypedResults.Ok(new PagedResult<string>(FirstNumbers, 2, 1, 2)))
                    .AllowAnonymous();
                endpoints.MapGet("/archived", (Page? page, PageSize? pageSize, InvoiceSortField? sortBy) =>
                    TypedResults.Ok(new PagedResult<string>(FirstNumbers, 2, 1, 2)))
                    .AllowAnonymous();
            }
        }
        """;

    private const string InvoiceNumberSortingTests = """
        using System.Text.Json.Nodes;

        namespace Shop.Integration.Tests;

        public class InvoiceNumberSortingTests(AspireFixture aspire)
        {
            [Fact]
            public async Task The_list_comes_back_in_the_order_asked_for_by_name()
            {
                using var client = aspire.CreateHostClient();

                var page = JsonNode.Parse(await client.GetStringAsync(new Uri("/billing/invoice-numbers?sortBy=Number&sortDirection=Descending&pageSize=3", UriKind.Relative), TestContext.Current.CancellationToken))!;

                page["items"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["INV-0025", "INV-0024", "INV-0023"]);
            }

            [Fact]
            public async Task The_openapi_document_names_the_sort_fields_and_directions()
            {
                using var client = aspire.CreateHostClient();

                var document = JsonNode.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken))!;

                var parameters = document["paths"]!["/billing/invoice-numbers"]!["get"]!["parameters"]!.AsArray()
                    .ToDictionary(parameter => parameter!["name"]!.GetValue<string>(), parameter => parameter!["schema"]!);
                Names(document, parameters["sortBy"]).ShouldBe(["Number", "IssuedOn"]);
                Names(document, parameters["sortDirection"]).ShouldBe(["Ascending", "Descending"]);
            }

            private static string[] Names(JsonNode document, JsonNode schema)
            {
                var resolved = schema["$ref"]?.GetValue<string>() is { } reference
                    ? document["components"]!["schemas"]![reference["#/components/schemas/".Length..]]!
                    : schema;
                // A schema without names (an enum described as an integer) has no list of values: none come back.
                return [.. resolved["enum"]?.AsArray().Select(value => value?.ToString() ?? "null") ?? []];
            }
        }
        """;

    [Fact]
    public void A_list_endpoint_whose_sort_parameters_are_numbers_or_come_alone_fails_the_integration_tests_and_a_named_one_sorts_over_http()
    {
        using var project = new GeneratedProject("lean-crud");
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/Queries/InvoiceNumbers/InvoiceSortField.cs", InvoiceSortField);
        project.Write("src/Modules/Billing/Billing.Application/Queries/InvoiceNumbers/DraftSortField.cs", DraftSortField);
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/InvoiceNumbers", ListInvoiceNumbers);
        project.Write("src/Modules/Billing/Billing.Api/InvoiceNumberEndpoints.cs", InvoiceNumberEndpoints);
        project.Write("tests/Integration.Tests/InvoiceNumberSortingTests.cs", InvoiceNumberSortingTests);
        project.Build();

        // The new endpoints also make the frontend's copy of the OpenAPI document stale, which another test reports.
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
            "Shop.Integration.Tests.ListEndpointsArePagedTests",
            "--filter-class",
            "Shop.Integration.Tests.InvoiceNumberSortingTests");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("GET /billing/drafts takes sortBy as a number; a sort field or direction is an enum the OpenAPI document names.");
        output.ShouldContain("GET /billing/archived takes sortBy without sortDirection; a list endpoint that sorts takes both.");
        output.ShouldNotContain("GET /billing/invoice-numbers takes");
        output.ShouldNotContain("GET /billing/drafts takes sortDirection");
        output.ShouldContain("failed: 1");
        output.ShouldContain("succeeded: 3");
    }
}
