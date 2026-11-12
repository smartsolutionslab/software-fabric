namespace Fabric.Packs.Tests;

/// <summary>
/// Paging: the Kernel's <c>Page</c>, <c>PageSize</c> and <c>PagedResult&lt;T&gt;</c> (base), and the REST pack's rule
/// that a list endpoint answers <c>PagedResult&lt;T&gt;</c> and takes <c>page</c> and <c>pageSize</c>.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class PagingTests
{
    private const string ListInvoiceNumbers = """
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.InvoiceNumbers;

        public sealed record ListInvoiceNumbers(Page Page, PageSize PageSize) : IQuery<PagedResult<string>>;

        public sealed class ListInvoiceNumbersHandler : IQueryHandler<ListInvoiceNumbers, PagedResult<string>>
        {
            private static readonly string[] All = [.. Enumerable.Range(1, 25).Select(number => $"INV-{number:D4}")];

            public Task<PagedResult<string>> HandleAsync(ListInvoiceNumbers query, CancellationToken cancellationToken)
            {
                var (page, pageSize) = query;
                string[] items = [.. All.Skip(page.Skip(pageSize)).Take(pageSize.Value)];
                return Task.FromResult(new PagedResult<string>(items, All.Length, page.Value, pageSize.Value));
            }
        }
        """;

    private const string ListInvoiceNumbersTests = """
        using Shop.Modules.Billing.Application.Queries.InvoiceNumbers;

        namespace Shop.Modules.Billing.Tests;

        public sealed class ListInvoiceNumbersTests
        {
            private readonly ListInvoiceNumbersHandler handler = new();

            [Fact]
            public async Task The_first_page_holds_the_first_numbers_and_counts_them_all()
            {
                PageSize.TryFrom(10, out var pageSize).ShouldBeTrue();

                var result = await handler.HandleAsync(new ListInvoiceNumbers(Page.First, pageSize!), TestContext.Current.CancellationToken);

                result.Items.Count.ShouldBe(10);
                result.Items[0].ShouldBe("INV-0001");
                result.TotalCount.ShouldBe(25);
                result.Page.ShouldBe(1);
                result.PageSize.ShouldBe(10);
            }

            [Fact]
            public async Task The_last_page_holds_what_is_left()
            {
                Page.TryFrom(3, out var page).ShouldBeTrue();
                PageSize.TryFrom(10, out var pageSize).ShouldBeTrue();

                var result = await handler.HandleAsync(new ListInvoiceNumbers(page!, pageSize!), TestContext.Current.CancellationToken);

                result.Items.Count.ShouldBe(5);
                result.Items[^1].ShouldBe("INV-0025");
            }

            [Fact]
            public void A_page_size_above_the_maximum_is_refused()
            {
                PageSize.TryFrom(PageSize.Maximum, out _).ShouldBeTrue();
                PageSize.TryFrom(PageSize.Maximum + 1, out _).ShouldBeFalse();
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
                endpoints.MapGet("/invoice-numbers", async (Page? page, PageSize? pageSize, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await dispatcher.QueryAsync(new ListInvoiceNumbers(page ?? Page.First, pageSize ?? PageSize.Default), cancellationToken)))
                    .AllowAnonymous();
                endpoints.MapGet("/all-invoice-numbers", () => TypedResults.Ok(FirstNumbers))
                    .AllowAnonymous();
                endpoints.MapGet("/invoice-numbers/first", () => TypedResults.Ok(new PagedResult<string>(FirstNumbers, 25, 1, 2)))
                    .AllowAnonymous();
                endpoints.MapGet("/invoice-count", () => TypedResults.Ok(25))
                    .AllowAnonymous();
            }
        }
        """;

    private const string InvoiceNumberPagingTests = """
        using System.Net;
        using System.Text.Json.Nodes;

        namespace Shop.Integration.Tests;

        public class InvoiceNumberPagingTests(AspireFixture aspire)
        {
            [Fact]
            public async Task The_first_and_the_last_page_come_back_over_http()
            {
                using var client = aspire.CreateHostClient();
                var cancellationToken = TestContext.Current.CancellationToken;

                var first = JsonNode.Parse(await client.GetStringAsync(new Uri("/billing/invoice-numbers?page=1&pageSize=10", UriKind.Relative), cancellationToken))!;
                var last = JsonNode.Parse(await client.GetStringAsync(new Uri("/billing/invoice-numbers?page=3&pageSize=10", UriKind.Relative), cancellationToken))!;

                first["items"]!.AsArray().Count.ShouldBe(10);
                first["items"]![0]!.GetValue<string>().ShouldBe("INV-0001");
                first["totalCount"]!.GetValue<int>().ShouldBe(25);
                last["items"]!.AsArray().Count.ShouldBe(5);
                last["items"]![4]!.GetValue<string>().ShouldBe("INV-0025");
                last["page"]!.GetValue<int>().ShouldBe(3);
            }

            [Fact]
            public async Task Without_paging_parameters_the_first_page_of_the_default_size_comes_back()
            {
                using var client = aspire.CreateHostClient();

                var page = JsonNode.Parse(await client.GetStringAsync(new Uri("/billing/invoice-numbers", UriKind.Relative), TestContext.Current.CancellationToken))!;

                page["page"]!.GetValue<int>().ShouldBe(1);
                page["pageSize"]!.GetValue<int>().ShouldBe(PageSize.Default.Value);
            }

            [Theory]
            [InlineData("pageSize=101")]
            [InlineData("pageSize=0")]
            [InlineData("page=0")]
            [InlineData("page=first")]
            public async Task Paging_outside_its_range_is_a_bad_request(string query)
            {
                using var client = aspire.CreateHostClient();

                using var response = await client.GetAsync(new Uri("/billing/invoice-numbers?" + query, UriKind.Relative), TestContext.Current.CancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            }

            [Fact]
            public async Task The_openapi_document_describes_the_paged_result_and_the_paging_parameters_as_integers()
            {
                using var client = aspire.CreateHostClient();

                var document = JsonNode.Parse(await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken))!;

                var operation = document["paths"]!["/billing/invoice-numbers"]!["get"]!;
                operation["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>()
                    .ShouldBe("#/components/schemas/PagedResultOfstring");
                var parameters = operation["parameters"]!.AsArray().ToDictionary(parameter => parameter!["name"]!.GetValue<string>(), parameter => parameter!["schema"]!);
                parameters["page"]["type"]!.GetValue<string>().ShouldBe("integer");
                parameters["page"]["minimum"]!.GetValue<int>().ShouldBe(1);
                parameters["pageSize"]["type"]!.GetValue<string>().ShouldBe("integer");
                parameters["pageSize"]["minimum"]!.GetValue<int>().ShouldBe(1);
                parameters["pageSize"]["maximum"]!.GetValue<int>().ShouldBe(PageSize.Maximum);
                document["components"]!["schemas"]!["PagedResultOfstring"]!["properties"]!.AsObject().Select(property => property.Key)
                    .ShouldBe(["items", "totalCount", "page", "pageSize"]);
            }
        }
        """;

    [Fact]
    public void A_module_test_pages_through_a_list_query_and_a_page_size_above_the_maximum_is_refused()
    {
        using var project = new GeneratedProject("saas-standard-react");
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/InvoiceNumbers", ListInvoiceNumbers);
        project.Write("tests/Modules/Billing.Tests/ListInvoiceNumbersTests.cs", ListInvoiceNumbersTests);
        project.Build();

        var (exitCode, output) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_list_endpoint_that_answers_a_plain_list_or_takes_no_paging_fails_the_integration_tests_and_a_paged_one_pages_over_http()
    {
        using var project = new GeneratedProject("lean-crud");
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/InvoiceNumbers", ListInvoiceNumbers);
        project.Write("src/Modules/Billing/Billing.Api/InvoiceNumberEndpoints.cs", InvoiceNumberEndpoints);
        project.Write("tests/Integration.Tests/InvoiceNumberPagingTests.cs", InvoiceNumberPagingTests);
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
            "Shop.Integration.Tests.InvoiceNumberPagingTests");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("GET /billing/all-invoice-numbers answers a plain list; a list endpoint answers PagedResult<T>.");
        output.ShouldContain("GET /billing/invoice-numbers/first answers PagedResult<T> but does not take page and pageSize from the query.");
        output.ShouldNotContain("GET /billing/invoice-numbers answers");
        output.ShouldNotContain("GET /billing/invoice-count");
        output.ShouldContain("failed: 1");
        // ListEndpointsArePagedTests' sorting check passes too: none of these endpoints sorts.
        output.ShouldContain("succeeded: 8");
    }
}
