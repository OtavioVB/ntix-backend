using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.GetCustomerOrders.Inputs;

/// <summary>Lista de pedidos do comprador (REQ13). O id do cliente vem do token; filtro e paginação são validados aqui.</summary>
public sealed record GetCustomerOrdersUseCaseInput
{
    public Guid? CustomerId { get; }

    public CustomerListFiltersValueObject Filters { get; }

    public CustomerListPageValueObject Page { get; }

    private GetCustomerOrdersUseCaseInput(Guid? customerId, CustomerListFiltersValueObject filters, CustomerListPageValueObject page)
    {
        CustomerId = customerId;
        Filters = filters;
        Page = page;
    }

    public static GetCustomerOrdersUseCaseInput Create(string? customerId, string? status, int? page, int? pageSize)
        => new(
            customerId: Guid.TryParse(customerId, out var parsed) ? parsed : null,
            filters: CustomerListFiltersValueObject.BuildOrderFilter(status),
            page: CustomerListPageValueObject.Build(page, pageSize, CustomerListFiltersValueObject.ORDER_FILTER_INVALID_NOTIFICATION_CODE));

    public MethodResult GetInputValidation()
        => MethodResult.Factory(Filters.MethodResult, Page.MethodResult);
}
