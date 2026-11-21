using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.GetCustomerTickets.Inputs;

/// <summary>Meus ingressos (REQ13): próximos ou passados, com paginação. O id do cliente vem do token.</summary>
public sealed record GetCustomerTicketsUseCaseInput
{
    public Guid? CustomerId { get; }

    public CustomerListFiltersValueObject Filters { get; }

    public CustomerListPageValueObject Page { get; }

    private GetCustomerTicketsUseCaseInput(Guid? customerId, CustomerListFiltersValueObject filters, CustomerListPageValueObject page)
    {
        CustomerId = customerId;
        Filters = filters;
        Page = page;
    }

    public static GetCustomerTicketsUseCaseInput Create(string? customerId, string? when, int? page, int? pageSize)
        => new(
            customerId: Guid.TryParse(customerId, out var parsed) ? parsed : null,
            filters: CustomerListFiltersValueObject.BuildTicketFilter(when),
            page: CustomerListPageValueObject.Build(page, pageSize, CustomerListFiltersValueObject.TICKET_FILTER_INVALID_NOTIFICATION_CODE));

    public MethodResult GetInputValidation()
        => MethodResult.Factory(Filters.MethodResult, Page.MethodResult);
}
