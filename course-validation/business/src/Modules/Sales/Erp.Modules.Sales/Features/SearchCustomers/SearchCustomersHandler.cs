using Dapper;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record SearchCustomersQuery(string? Search, int Page, int PageSize) : IQuery<PagedResult<CustomerDto>>;

internal sealed record GetCustomerQuery(long CustomerId) : IQuery<CustomerDto?>;

// Available credit is what the next order may use: the limit, minus what they owe, minus what's awaiting approval.
internal sealed record CustomerDto(
    long Id, string Name, string? Tin, string? Address, int PaymentTermsDays, bool IsBlocked,
    decimal CreditLimit, decimal OutstandingBalance, decimal PendingTotal, decimal AvailableCredit);

internal sealed class SearchCustomersValidator : AbstractValidator<SearchCustomersQuery>
{
    public SearchCustomersValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}

internal sealed class SearchCustomersHandler(IDbConnectionFactory connections)
    : IQueryHandler<SearchCustomersQuery, PagedResult<CustomerDto>>, IQueryHandler<GetCustomerQuery, CustomerDto?>
{
    public async ValueTask<PagedResult<CustomerDto>> Handle(SearchCustomersQuery q, CancellationToken ct)
    {
        var search = SearchText.ForLike(q.Search);
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM sales.search_customers(NULL, @Search, @Page, @PageSize);
            SELECT sales.count_customers(@Search);
            """, new { Search = search, q.Page, q.PageSize }, cancellationToken: ct));

        var rows = (await multi.ReadAsync<CustomerDto>()).ToList();
        return new PagedResult<CustomerDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }

    public async ValueTask<CustomerDto?> Handle(GetCustomerQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<CustomerDto>(new CommandDefinition(
            "SELECT * FROM sales.search_customers(@CustomerId, NULL, 1, 1);", new { q.CustomerId }, cancellationToken: ct));
    }
}
