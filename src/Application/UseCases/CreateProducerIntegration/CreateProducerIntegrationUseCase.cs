using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Payments;
using Application.UseCases.Base.Payments.Outputs;
using Application.UseCases.CreateProducerIntegration.Inputs;
using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.Cryptography.Credentials.Interfaces;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Enums;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.CreateProducerIntegration;

/// <summary>Conecta a produtora a um fornecedor do catálogo, com as credenciais da conta dela cifradas no banco.</summary>
public sealed class CreateProducerIntegrationUseCase : IUseCase<CreateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IPaymentProviderRepository _paymentProviderRepository;
    private readonly IProducerIntegrationRepository _producerIntegrationRepository;
    private readonly ICredentialCipher _credentialCipher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CreateProducerIntegrationDiagnostics _diagnostics;
    private readonly ILogger<CreateProducerIntegrationUseCase> _logger;

    public CreateProducerIntegrationUseCase(
        IProducerRepository producerRepository,
        IPaymentProviderRepository paymentProviderRepository,
        IProducerIntegrationRepository producerIntegrationRepository,
        ICredentialCipher credentialCipher,
        IUnitOfWork unitOfWork,
        CreateProducerIntegrationDiagnostics diagnostics,
        ILogger<CreateProducerIntegrationUseCase> logger)
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

    private const string PROVIDER_NOT_FOUND_NOTIFICATION_CODE = "PROVIDER_NOT_FOUND";
    private const string PROVIDER_NOT_FOUND_NOTIFICATION_MESSAGE = "O fornecedor de pagamento informado não existe ou não está disponível.";

    public const string INTEGRATION_NAME_ALREADY_EXISTS_NOTIFICATION_CODE = "INTEGRATION_NAME_ALREADY_EXISTS";
    private const string INTEGRATION_NAME_ALREADY_EXISTS_NOTIFICATION_MESSAGE = "Já existe uma integração com este nome.";

    public const string CREATE_PRODUCER_INTEGRATION_CANCELED_NOTIFICATION_CODE = "CREATE_PRODUCER_INTEGRATION_CANCELED";
    private const string CREATE_PRODUCER_INTEGRATION_CANCELED_NOTIFICATION_MESSAGE = "A conexão do fornecedor de pagamento foi cancelada.";

    public async Task<MethodResult<ProducerIntegrationOutput>> ExecuteAsync(CreateProducerIntegrationUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = CreateProducerIntegrationDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("CreateProducerIntegration", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return BuildInvalidDataResult(input, validationResult);

            var producerSlug = input.ProducerSlug.Value;

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var producerId = await _producerRepository.GetIdBySlugAsync(producerSlug, cancellationToken);

            if (producerId is null)
                return await RejectProducerNotFoundAsync(producerSlug);

            // O produtor só conecta fornecedor habilitado; o desligado continua no catálogo para as integrações existentes.
            var provider = (await _paymentProviderRepository.GetAllAsync(cancellationToken))
                .Select(PaymentConversions.ToProvider)
                .FirstOrDefault(provider => provider.Code == input.ProviderCode && provider.IsEnabled);

            if (provider is null)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                return BuildInvalidDataResult(input, MethodResult.Error([Notification.Error(PROVIDER_NOT_FOUND_NOTIFICATION_CODE, PROVIDER_NOT_FOUND_NOTIFICATION_MESSAGE)]));
            }

            var now = DateTimeValueObject.Build(DateTime.UtcNow);
            var correlationId = IdValueObject.Build(auditableInfo.CorrelationId.ToString());

            var integrationResult = ProducerIntegration.Create(
                producerId: IdValueObject.Build(producerId.Value.ToString()),
                provider: provider,
                name: input.Name,
                credentials: input.Credentials,
                now: now,
                correlationId: correlationId);

            if (integrationResult.IsError)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                return BuildInvalidDataResult(input, integrationResult);
            }

            var integration = integrationResult.Output!;
            var credentials = input.Credentials
                .Select(credential => PaymentConversions.EncryptCredential(_credentialCipher, integration, credential.Key, credential.Value!.Value))
                .ToArray();

            var creationResult = await _producerIntegrationRepository.CreateAsync(PaymentConversions.ToModel(integration, credentials), cancellationToken);

            if (creationResult == ProducerIntegrationCreationResult.NameAlreadyExists)
                return await RejectNameAlreadyExistsAsync(producerSlug);

            await _unitOfWork.CommitAsync(cancellationToken);

            return BuildSuccessResult(producerSlug, integration, credentials);
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return BuildCanceledResult();

            CreateProducerIntegrationLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(CreateProducerIntegrationDiagnostics.OUTCOME_ERROR);

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
            CreateProducerIntegrationLog.RollbackFailed(_logger, ex);
        }
    }

    private MethodResult<ProducerIntegrationOutput> BuildSuccessResult(string producerSlug, ProducerIntegration integration, IReadOnlyList<ProducerIntegrationCredentialModel> credentials)
    {
        RecordOutcome(CreateProducerIntegrationDiagnostics.OUTCOME_CREATED);

        Activity.Current?.SetTag("ntix.payments.integration_id", integration.Id.Value);

        CreateProducerIntegrationLog.Created(
            logger: _logger,
            integrationId: integration.Id.Value,
            producerSlug: producerSlug,
            providerCode: integration.Provider.Code,
            credentialKeys: string.Join(", ", integration.CredentialKeys.Order(StringComparer.Ordinal)));

        return MethodResult<ProducerIntegrationOutput>.Success(PaymentConversions.ToOutput(integration, credentials, _credentialCipher));
    }

    private MethodResult<ProducerIntegrationOutput> BuildInvalidDataResult(CreateProducerIntegrationUseCaseInput input, MethodResult validationResult)
    {
        RecordOutcome(CreateProducerIntegrationDiagnostics.OUTCOME_VALIDATION_FAILED);

        CreateProducerIntegrationLog.InvalidData(
            logger: _logger,
            producerSlug: input.ProducerSlug.IsValid ? input.ProducerSlug.Value : "-",
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return MethodResult<ProducerIntegrationOutput>.Error(validationResult.Notifications);
    }

    private async Task<MethodResult<ProducerIntegrationOutput>> RejectProducerNotFoundAsync(string producerSlug)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        RecordOutcome(CreateProducerIntegrationDiagnostics.OUTCOME_PRODUCER_NOT_FOUND);

        CreateProducerIntegrationLog.ProducerNotFound(_logger, producerSlug);

        return MethodResult<ProducerIntegrationOutput>.Error([Notification.Error(PRODUCER_NOT_FOUND_NOTIFICATION_CODE, PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE)]);
    }

    private async Task<MethodResult<ProducerIntegrationOutput>> RejectNameAlreadyExistsAsync(string producerSlug)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        RecordOutcome(CreateProducerIntegrationDiagnostics.OUTCOME_NAME_ALREADY_EXISTS);

        CreateProducerIntegrationLog.NameAlreadyExists(_logger, producerSlug);

        return MethodResult<ProducerIntegrationOutput>.Error([Notification.Error(INTEGRATION_NAME_ALREADY_EXISTS_NOTIFICATION_CODE, INTEGRATION_NAME_ALREADY_EXISTS_NOTIFICATION_MESSAGE)]);
    }

    private static MethodResult<ProducerIntegrationOutput> BuildCanceledResult()
        => MethodResult<ProducerIntegrationOutput>.Error([Notification.Error(CREATE_PRODUCER_INTEGRATION_CANCELED_NOTIFICATION_CODE, CREATE_PRODUCER_INTEGRATION_CANCELED_NOTIFICATION_MESSAGE)]);
}
