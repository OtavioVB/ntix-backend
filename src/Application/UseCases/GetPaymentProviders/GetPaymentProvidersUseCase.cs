using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Payments;
using Application.UseCases.GetPaymentProviders.Inputs;
using Application.UseCases.GetPaymentProviders.Outputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetPaymentProviders;

/// <summary>Catálogo dos fornecedores habilitados, para a aba Integrações montar o formulário de cada um.</summary>
public sealed class GetPaymentProvidersUseCase : IUseCase<GetPaymentProvidersUseCaseInput, GetPaymentProvidersUseCaseOutput>
{
    private readonly IPaymentProviderRepository _paymentProviderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly GetPaymentProvidersDiagnostics _diagnostics;
    private readonly ILogger<GetPaymentProvidersUseCase> _logger;

    public GetPaymentProvidersUseCase(
        IPaymentProviderRepository paymentProviderRepository,
        IUnitOfWork unitOfWork,
        GetPaymentProvidersDiagnostics diagnostics,
        ILogger<GetPaymentProvidersUseCase> logger)
    {
        _paymentProviderRepository = paymentProviderRepository;
        _unitOfWork = unitOfWork;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string GET_PAYMENT_PROVIDERS_CANCELED_NOTIFICATION_CODE = "GET_PAYMENT_PROVIDERS_CANCELED";
    private const string GET_PAYMENT_PROVIDERS_CANCELED_NOTIFICATION_MESSAGE = "A consulta dos fornecedores de pagamento foi cancelada.";

    public async Task<MethodResult<GetPaymentProvidersUseCaseOutput>> ExecuteAsync(GetPaymentProvidersUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetPaymentProvidersDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetPaymentProviders", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return BuildInvalidDataResult(validationResult);

            // Só leitura: a transação dá uma conexão ao repositório e é desfeita no fim, sem nada a gravar.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var providers = (await _paymentProviderRepository.GetAllAsync(cancellationToken))
                .Select(PaymentConversions.ToProvider)
                .Where(provider => provider.IsEnabled)
                .ToArray();

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            RecordOutcome(GetPaymentProvidersDiagnostics.OUTCOME_SERVED);

            GetPaymentProvidersLog.Served(_logger, providers.Length);

            var output = new GetPaymentProvidersUseCaseOutput(providers
                .Select(provider => new PaymentProviderOutput(
                    Code: provider.Code,
                    Name: provider.Name,
                    PaymentMethods: provider.PaymentMethods.Order().Select(paymentMethod => paymentMethod.ToString()).ToArray(),
                    CredentialKeys: provider.CredentialKeys
                        .Select(credentialKey => new PaymentProviderCredentialKeyOutput(credentialKey.Key, credentialKey.IsSecret, credentialKey.IsRequired))
                        .ToArray()))
                .ToArray());

            return MethodResult<GetPaymentProvidersUseCaseOutput>.Success(output);
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return BuildCanceledResult();

            GetPaymentProvidersLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(GetPaymentProvidersDiagnostics.OUTCOME_ERROR);

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
        catch (Exception rollbackException)
        {
            GetPaymentProvidersLog.RollbackFailed(_logger, rollbackException);
        }
    }

    private MethodResult<GetPaymentProvidersUseCaseOutput> BuildInvalidDataResult(MethodResult validationResult)
    {
        RecordOutcome(GetPaymentProvidersDiagnostics.OUTCOME_VALIDATION_FAILED);

        GetPaymentProvidersLog.InvalidData(
            logger: _logger,
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return MethodResult<GetPaymentProvidersUseCaseOutput>.Error(validationResult.Notifications);
    }

    private static MethodResult<GetPaymentProvidersUseCaseOutput> BuildCanceledResult()
    {
        var canceledNotification = Notification.Error(
            code: GET_PAYMENT_PROVIDERS_CANCELED_NOTIFICATION_CODE,
            message: GET_PAYMENT_PROVIDERS_CANCELED_NOTIFICATION_MESSAGE);

        return MethodResult<GetPaymentProvidersUseCaseOutput>.Error([canceledNotification]);
    }
}
