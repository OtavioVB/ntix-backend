using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Payments;
using Application.UseCases.GetProducerIntegrations.Inputs;
using Application.UseCases.GetProducerIntegrations.Outputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.Cryptography.Credentials.Interfaces;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetProducerIntegrations;

/// <summary>Integrações da produtora, com as credenciais secretas só pelos últimos caracteres (RN10).</summary>
public sealed class GetProducerIntegrationsUseCase : IUseCase<GetProducerIntegrationsUseCaseInput, GetProducerIntegrationsUseCaseOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IPaymentProviderRepository _paymentProviderRepository;
    private readonly IProducerIntegrationRepository _producerIntegrationRepository;
    private readonly ICredentialCipher _credentialCipher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly GetProducerIntegrationsDiagnostics _diagnostics;
    private readonly ILogger<GetProducerIntegrationsUseCase> _logger;

    public GetProducerIntegrationsUseCase(
        IProducerRepository producerRepository,
        IPaymentProviderRepository paymentProviderRepository,
        IProducerIntegrationRepository producerIntegrationRepository,
        ICredentialCipher credentialCipher,
        IUnitOfWork unitOfWork,
        GetProducerIntegrationsDiagnostics diagnostics,
        ILogger<GetProducerIntegrationsUseCase> logger)
    {
        _producerRepository = producerRepository;
        _paymentProviderRepository = paymentProviderRepository;
        _producerIntegrationRepository = producerIntegrationRepository;
        _credentialCipher = credentialCipher;
        _unitOfWork = unitOfWork;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string PRODUCER_NOT_FOUND_NOTIFICATION_CODE = "PRODUCER_NOT_FOUND";
    private const string PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE = "A produtora informada não existe.";

    public const string GET_PRODUCER_INTEGRATIONS_CANCELED_NOTIFICATION_CODE = "GET_PRODUCER_INTEGRATIONS_CANCELED";
    private const string GET_PRODUCER_INTEGRATIONS_CANCELED_NOTIFICATION_MESSAGE = "A consulta das integrações de pagamento foi cancelada.";

    public async Task<MethodResult<GetProducerIntegrationsUseCaseOutput>> ExecuteAsync(GetProducerIntegrationsUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetProducerIntegrationsDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetProducerIntegrations", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return BuildInvalidDataResult(validationResult);

            var producerSlug = input.ProducerSlug.Value;

            // Só leitura: a transação dá uma conexão aos repositórios e uma leitura consistente, e é desfeita no fim.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var producerId = await _producerRepository.GetIdBySlugAsync(producerSlug, cancellationToken);

            if (producerId is null)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                RecordOutcome(GetProducerIntegrationsDiagnostics.OUTCOME_PRODUCER_NOT_FOUND);

                GetProducerIntegrationsLog.ProducerNotFound(_logger, producerSlug);

                return MethodResult<GetProducerIntegrationsUseCaseOutput>.Error([Notification.Error(PRODUCER_NOT_FOUND_NOTIFICATION_CODE, PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE)]);
            }

            var providersById = PaymentConversions.ToProvidersById(await _paymentProviderRepository.GetAllAsync(cancellationToken));
            var models = await _producerIntegrationRepository.GetByProducerAsync(producerId.Value, cancellationToken);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            var integrations = models
                .Select(model => PaymentConversions.ToOutput(PaymentConversions.ToIntegration(model, providersById), model.Credentials, _credentialCipher))
                .ToArray();

            RecordOutcome(GetProducerIntegrationsDiagnostics.OUTCOME_SERVED);

            GetProducerIntegrationsLog.Served(_logger, producerSlug, integrations.Length);

            return MethodResult<GetProducerIntegrationsUseCaseOutput>.Success(new GetProducerIntegrationsUseCaseOutput(integrations));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<GetProducerIntegrationsUseCaseOutput>.Error([Notification.Error(GET_PRODUCER_INTEGRATIONS_CANCELED_NOTIFICATION_CODE, GET_PRODUCER_INTEGRATIONS_CANCELED_NOTIFICATION_MESSAGE)]);

            GetProducerIntegrationsLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(GetProducerIntegrationsDiagnostics.OUTCOME_ERROR);

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
            GetProducerIntegrationsLog.RollbackFailed(_logger, ex);
        }
    }

    private MethodResult<GetProducerIntegrationsUseCaseOutput> BuildInvalidDataResult(MethodResult validationResult)
    {
        RecordOutcome(GetProducerIntegrationsDiagnostics.OUTCOME_VALIDATION_FAILED);

        GetProducerIntegrationsLog.InvalidData(
            logger: _logger,
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return MethodResult<GetProducerIntegrationsUseCaseOutput>.Error(validationResult.Notifications);
    }
}
