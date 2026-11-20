using Application.UseCases.Base;
using Application.UseCases.Base.Customers;
using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetCurrentCustomer.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetCurrentCustomer;

/// <summary>Meus dados (REQ12): o cadastro do cliente do access token, com o CPF ofuscado (RN10).</summary>
public sealed class GetCurrentCustomerUseCase : IUseCase<GetCurrentCustomerUseCaseInput, CurrentCustomerOutput>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly GetCurrentCustomerDiagnostics _diagnostics;
    private readonly ILogger<GetCurrentCustomerUseCase> _logger;

    public GetCurrentCustomerUseCase(ICustomerRepository customerRepository, IUnitOfWork unitOfWork, GetCurrentCustomerDiagnostics diagnostics, ILogger<GetCurrentCustomerUseCase> logger)
    {
        _customerRepository = customerRepository;
        _unitOfWork = unitOfWork;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string CUSTOMER_NOT_FOUND_NOTIFICATION_CODE = "CUSTOMER_NOT_FOUND";
    private const string CUSTOMER_NOT_FOUND_NOTIFICATION_MESSAGE = "A conta do cliente não foi encontrada.";

    public const string GET_CURRENT_CUSTOMER_CANCELED_NOTIFICATION_CODE = "GET_CURRENT_CUSTOMER_CANCELED";
    private const string GET_CURRENT_CUSTOMER_CANCELED_NOTIFICATION_MESSAGE = "A consulta dos seus dados foi cancelada.";

    public async Task<MethodResult<CurrentCustomerOutput>> ExecuteAsync(GetCurrentCustomerUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetCurrentCustomerDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetCurrentCustomer", auditableInfo);

        _diagnostics.Requested();

        try
        {
            if (auditableInfo.MethodResult.IsError)
            {
                RecordOutcome(GetCurrentCustomerDiagnostics.OUTCOME_VALIDATION_FAILED);

                return MethodResult<CurrentCustomerOutput>.Error(auditableInfo.MethodResult.Notifications);
            }

            // Só leitura: a transação dá uma conexão ao repositório e é desfeita no fim.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var customer = input.CustomerId is null ? null : await _customerRepository.GetByIdAsync(input.CustomerId.Value, cancellationToken);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            if (customer is null)
            {
                RecordOutcome(GetCurrentCustomerDiagnostics.OUTCOME_CUSTOMER_NOT_FOUND);

                GetCurrentCustomerLog.NotFound(_logger);

                return MethodResult<CurrentCustomerOutput>.Error([Notification.Error(CUSTOMER_NOT_FOUND_NOTIFICATION_CODE, CUSTOMER_NOT_FOUND_NOTIFICATION_MESSAGE)]);
            }

            RecordOutcome(GetCurrentCustomerDiagnostics.OUTCOME_SERVED);

            GetCurrentCustomerLog.Served(_logger, customer.Id);

            return MethodResult<CurrentCustomerOutput>.Success(CustomerSessionConversions.ToCurrent(customer));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<CurrentCustomerOutput>.Error([Notification.Error(GET_CURRENT_CUSTOMER_CANCELED_NOTIFICATION_CODE, GET_CURRENT_CUSTOMER_CANCELED_NOTIFICATION_MESSAGE)]);

            GetCurrentCustomerLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(GetCurrentCustomerDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private void RecordOutcome(string outcome)
    {
        _diagnostics.Finished(outcome);

        Activity.Current?.SetTag("ntix.outcome", outcome);
    }

    private async Task RollbackAsync()
    {
        try
        {
            await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            GetCurrentCustomerLog.RollbackFailed(_logger, ex);
        }
    }
}
