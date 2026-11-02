namespace Fabric.Packs.Tests;

/// <summary>
/// Logical CQRS: a model becomes a DTO through the <c>To&lt;Name&gt;Dto()</c> extensions of its <c>&lt;Model&gt;Mappings</c>
/// class; a handler never builds one itself.
/// </summary>
public sealed class DtoMappingTests
{
    private const string Invoices = """
        using System.Linq.Expressions;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.Invoices;

        public sealed record InvoiceRow(string Number, decimal Total);

        public sealed record InvoiceDto(string Number, decimal Total);

        public static class InvoiceRowMappings
        {
            public static InvoiceDto ToInvoiceDto(this InvoiceRow row) => ToInvoiceDtoExpression().Compile()(row);

            public static Expression<Func<InvoiceRow, InvoiceDto>> ToInvoiceDtoExpression() => row => new InvoiceDto(row.Number, row.Total);
        }

        public sealed record GetInvoice(string Number) : IQuery<Result<InvoiceDto, ApiError>>;

        public sealed record ListInvoices : IQuery<Result<IReadOnlyList<InvoiceDto>, ApiError>>;

        public sealed record FindInvoice(string Number) : IQuery<Result<InvoiceDto, ApiError>>;

        public sealed record SearchInvoices(decimal Minimum) : IQuery<Result<IReadOnlyList<InvoiceDto>, ApiError>>;

        public sealed record LatestInvoice : IQuery<Result<InvoiceDto, ApiError>>;

        public sealed class GetInvoiceHandler : IQueryHandler<GetInvoice, Result<InvoiceDto, ApiError>>
        {
            public async Task<Result<InvoiceDto, ApiError>> HandleAsync(GetInvoice query, CancellationToken cancellationToken)
            {
                await Task.Yield();
                return Success(new InvoiceRow(query.Number, 10m).ToInvoiceDto());
            }
        }

        public sealed class ListInvoicesHandler : IQueryHandler<ListInvoices, Result<IReadOnlyList<InvoiceDto>, ApiError>>
        {
            private static readonly InvoiceRow[] Rows = [new("INV-1", 10m), new("INV-2", 20m)];

            public async Task<Result<IReadOnlyList<InvoiceDto>, ApiError>> HandleAsync(ListInvoices query, CancellationToken cancellationToken)
            {
                await Task.Yield();
                InvoiceDto[] invoices = [.. Rows.AsQueryable().Select(InvoiceRowMappings.ToInvoiceDtoExpression())];
                return Success<IReadOnlyList<InvoiceDto>>(invoices);
            }
        }

        public sealed class FindInvoiceHandler : IQueryHandler<FindInvoice, Result<InvoiceDto, ApiError>>
        {
            public async Task<Result<InvoiceDto, ApiError>> HandleAsync(FindInvoice query, CancellationToken cancellationToken)
            {
                await Task.Yield();
                return Success(new InvoiceDto(query.Number, 10m));
            }
        }

        public sealed class SearchInvoicesHandler : IQueryHandler<SearchInvoices, Result<IReadOnlyList<InvoiceDto>, ApiError>>
        {
            private static readonly InvoiceRow[] Rows = [new("INV-1", 10m), new("INV-2", 20m)];

            public async Task<Result<IReadOnlyList<InvoiceDto>, ApiError>> HandleAsync(SearchInvoices query, CancellationToken cancellationToken)
            {
                await Task.Yield();
                InvoiceDto[] invoices = [.. Rows.AsQueryable().Where(row => row.Total >= query.Minimum).Select(row => new InvoiceDto(row.Number, row.Total))];
                return Success<IReadOnlyList<InvoiceDto>>(invoices);
            }
        }

        public sealed class LatestInvoiceHandler : IQueryHandler<LatestInvoice, Result<InvoiceDto, ApiError>>
        {
            public async Task<Result<InvoiceDto, ApiError>> HandleAsync(LatestInvoice query, CancellationToken cancellationToken)
            {
                await Task.Yield();
                return Success(ToDto(new InvoiceRow("INV-9", 90m)));
            }

            private static InvoiceDto ToDto(InvoiceRow row)
            {
                InvoiceDto dto = new(row.Number, row.Total);
                return dto;
            }
        }
        """;

    [Fact]
    public void A_handler_that_builds_a_dto_fails_the_architecture_tests_and_one_that_uses_the_mappings_passes()
    {
        using var project = new GeneratedProject("saas-standard-react");
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/Invoices", Invoices);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application.Queries.Invoices.FindInvoiceHandler builds InvoiceDto itself;");
        output.ShouldContain("Shop.Modules.Billing.Application.Queries.Invoices.SearchInvoicesHandler builds InvoiceDto itself;");
        output.ShouldContain("Shop.Modules.Billing.Application.Queries.Invoices.LatestInvoiceHandler builds InvoiceDto itself;");
        output.ShouldNotContain("GetInvoiceHandler builds");
        output.ShouldNotContain("ListInvoicesHandler builds");
        output.ShouldNotContain("InvoiceRowMappings builds");
        output.ShouldContain("failed: 1");
    }

    private const string Totals = """
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.Totals;

        public record struct TotalDto
        {
            public decimal Total { get; init; }
        }

        public sealed record GetTotal(decimal Total) : IQuery<Result<TotalDto, ApiError>>;

        public sealed class GetTotalHandler : IQueryHandler<GetTotal, Result<TotalDto, ApiError>>
        {
            public async Task<Result<TotalDto, ApiError>> HandleAsync(GetTotal query, CancellationToken cancellationToken)
            {
                await Task.Yield();
                return Success(new TotalDto { Total = query.Total });
            }
        }
        """;

    [Fact]
    public void A_handler_that_builds_a_struct_dto_with_an_object_initializer_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject("saas-standard-react");
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/Totals", Totals);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application.Queries.Totals.GetTotalHandler builds TotalDto itself;");
    }

    private const string Shares = """
        using System.Linq.Expressions;
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.Shares;

        public record struct ShareDto
        {
            public decimal Share { get; init; }
        }

        public static class ShareMappings
        {
            public static ShareDto ToShareDto(this decimal share) => new() { Share = share };
        }

        public sealed record DescribeShare(decimal Share) : IQuery<Result<string, ApiError>>;

        public sealed record ListShares : IQuery<Result<IReadOnlyList<ShareDto>, ApiError>>;

        public sealed class DescribeShareHandler : IQueryHandler<DescribeShare, Result<string, ApiError>>
        {
            public async Task<Result<string, ApiError>> HandleAsync(DescribeShare query, CancellationToken cancellationToken)
            {
                await Task.Yield();
                return Success($"{typeof(ShareDto).Name} {query.Share.ToShareDto().Share}");
            }
        }

        public sealed class ListSharesHandler : IQueryHandler<ListShares, Result<IReadOnlyList<ShareDto>, ApiError>>
        {
            private static readonly decimal[] Shares = [0.25m, 0.75m];

            public async Task<Result<IReadOnlyList<ShareDto>, ApiError>> HandleAsync(ListShares query, CancellationToken cancellationToken)
            {
                await Task.Yield();
                ShareDto[] shares = [.. Shares.AsQueryable().Select(share => new ShareDto { Share = share })];
                return Success<IReadOnlyList<ShareDto>>(shares);
            }
        }
        """;

    [Fact]
    public void A_struct_dto_built_in_an_expression_tree_fails_the_architecture_tests_and_a_typeof_of_one_does_not()
    {
        using var project = new GeneratedProject("saas-standard-react");
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/Shares", Shares);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application.Queries.Shares.ListSharesHandler builds ShareDto itself;");
        output.ShouldNotContain("DescribeShareHandler builds");
    }
}
