using Application.UseCases.Base;
using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetCustomerOrders.Inputs;
using Domain.BoundedContexts.OrderContext.Entities;
using Domain.BoundedContexts.OrderContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetCustomerOrders;

/// <summary>
/// Meus pedidos (REQ13): pedidos do comprador do mais recente ao mais antigo, com o status efetivo (RN3) e o filtro por ele. Só os
/// pedidos do próprio cliente (RN1); a leitura não abre transação de escrita.
/// </summary>
public sealed class GetCustomerOrdersUseCase : IUseCase<GetCustomerOrdersUseCaseInput, CustomerOrdersPageOutput>
{
    public const string CUSTOMER_NOT_FOUND_NOTIFICATION_CODE = "CUSTOMER_NOT_FOUND";
    private const string CUSTOMER_NOT_FOUND_NOTIFICATION_MESSAGE = "O cliente da sessão não existe.";

    public const string GET_CUSTOMER_ORDERS_CANCELED_NOTIFICATION_CODE = "GET_CUSTOMER_ORDERS_CANCELED";
    private const string GET_CUSTOMER_ORDERS_CANCELED_NOTIFICATION_MESSAGE = "A consulta dos pedidos foi cancelada.";

    private readonly IOrderRepository _orderRepository;
    private readonly IProducerRepository _producerRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly GetCustomerOrdersDiagnostics _diagnostics;
    private readonly ILogger<GetCustomerOrdersUseCase> _logger;

    public GetCustomerOrdersUseCase(
        IOrderRepository orderRepository,
        IProducerRepository producerRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        GetCustomerOrdersDiagnostics diagnostics,
        ILogger<GetCustomerOrdersUseCase> logger)
    {
        _orderRepository = orderRepository;
        _producerRepository = producerRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public async Task<MethodResult<CustomerOrdersPageOutput>> ExecuteAsync(GetCustomerOrdersUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetCustomerOrdersDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetCustomerOrders", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
            {
                _diagnostics.Finished(GetCustomerOrdersDiagnostics.OUTCOME_VALIDATION_FAILED);

                return MethodResult<CustomerOrdersPageOutput>.Error(validationResult.Notifications);
            }

            if (input.CustomerId is null)
            {
                GetCustomerOrdersLog.CustomerNotFound(_logger);
                _diagnostics.Finished(GetCustomerOrdersDiagnostics.OUTCOME_CUSTOMER_NOT_FOUND);

                return MethodResult<CustomerOrdersPageOutput>.Error([Notification.Error(CUSTOMER_NOT_FOUND_NOTIFICATION_CODE, CUSTOMER_NOT_FOUND_NOTIFICATION_MESSAGE)]);
            }

            // O PostgreSQL guarda microssegundos: o instante é truncado para o status efetivo ser o mesmo da leitura.
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            now = now.AddTicks(-(now.Ticks % 10));

            var filter = input.Filters.OrderStatus?.ToString();

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var page = await _orderRepository.ListByCustomerAsync(input.CustomerId.Value, filter, now, input.Page.Offset, input.Page.PageSize, cancellationToken);
            var producers = await _producerRepository.GetProfilesByIdsAsync(page.Items.Select(item => item.ProducerId).Distinct().ToArray(), cancellationToken);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            var output = new CustomerOrdersPageOutput(
                Items: page.Items.Select(item => ToItem(item, producers, now)).ToArray(),
                Page: input.Page.Page,
                PageSize: input.Page.PageSize,
                TotalItems: page.TotalItems);

            GetCustomerOrdersLog.Served(_logger, filter ?? "TODOS", output.Page, output.Items.Count, output.TotalItems);
            _diagnostics.Finished(GetCustomerOrdersDiagnostics.OUTCOME_SERVED);

            return MethodResult<CustomerOrdersPageOutput>.Success(output);
        }
        catch (Exception ex) when (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            await RollbackAsync();

            _diagnostics.Finished(GetCustomerOrdersDiagnostics.OUTCOME_CANCELED);

            return MethodResult<CustomerOrdersPageOutput>.Error([Notification.Error(GET_CUSTOMER_ORDERS_CANCELED_NOTIFICATION_CODE, GET_CUSTOMER_ORDERS_CANCELED_NOTIFICATION_MESSAGE)]);
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            GetCustomerOrdersLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            _diagnostics.Finished(GetCustomerOrdersDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    // Status efetivo (RN3): o mesmo cálculo do pedido (Order.EffectiveStatusOf), na mesma hora usada pelo filtro.
    private static CustomerOrderItemOutput ToItem(CustomerOrderRowModel row, IReadOnlyList<ProducerProfileModel> producers, DateTime now)
    {
        var producer = producers.FirstOrDefault(profile => profile.Id == row.ProducerId);
        var effective = Order.EffectiveStatusOf(Enum.Parse<OrderStatus>(row.Status), row.ExpiresAt, now);

        return new CustomerOrderItemOutput(
            Code: row.Code,
            Status: effective.ToString(),
            CreatedAt: DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc),
            PaidAt: row.PaidAt is null ? null : DateTime.SpecifyKind(row.PaidAt.Value, DateTimeKind.Utc),
            TotalCents: (long)decimal.Round(row.TotalAmount * 100m, 0, MidpointRounding.AwayFromZero),
            TicketCount: row.TicketCount,
            Event: new CustomerOrderEventOutput(row.EventSlug, row.EventTitle, DateTime.SpecifyKind(row.EventStartsAt, DateTimeKind.Utc), row.EventCity, row.EventState, row.EventImageUrl),
            Producer: new CustomerProducerOutput(producer?.Slug ?? string.Empty, producer?.Name ?? string.Empty));
    }

    private async Task RollbackAsync()
    {
        try
        {
            await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            GetCustomerOrdersLog.RollbackFailed(_logger, ex);
        }
    }
}
