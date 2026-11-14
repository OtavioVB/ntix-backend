using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Payments;
using Application.UseCases.Base.Payments.Outputs;
using Application.UseCases.UpdateProducerIntegration.Inputs;
using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.BoundedContexts.PaymentContext.Enums;
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

namespace Application.UseCases.UpdateProducerIntegration;

/// <summary>
/// Altera nome, situação e credenciais de uma integração da produtora. Inativar tira a integração do ranqueamento,
/// sem tirá-la da rota (RN11); a prioridade nova vale só para as próximas cobranças.
/// </summary>
public sealed class UpdateProducerIntegrationUseCase : IUseCase<UpdateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IPaymentProviderRepository _paymentProviderRepository;
    private readonly IProducerIntegrationRepository _producerIntegrationRepository;
    private readonly ICredentialCipher _credentialCipher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UpdateProducerIntegrationDiagnostics _diagnostics;
    private readonly ILogger<UpdateProducerIntegrationUseCase> _logger;

    public UpdateProducerIntegrationUseCase(
        IProducerRepository producerRepository,
        IPaymentProviderRepository paymentProviderRepository,
        IProducerIntegrationRepository producerIntegrationRepository,
        ICredentialCipher credentialCipher,
        IUnitOfWork unitOfWork,
        UpdateProducerIntegrationDiagnostics diagnostics,
        ILogger<UpdateProducerIntegrationUseCase> logger)
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

    public const string INTEGRATION_NOT_FOUND_NOTIFICATION_CODE = "INTEGRATION_NOT_FOUND";
    private const string INTEGRATION_NOT_FOUND_NOTIFICATION_MESSAGE = "A integração informada não existe para a produtora.";

    public const string INTEGRATION_NAME_ALREADY_EXISTS_NOTIFICATION_CODE = "INTEGRATION_NAME_ALREADY_EXISTS";
    private const string INTEGRATION_NAME_ALREADY_EXISTS_NOTIFICATION_MESSAGE = "Já existe uma integração com este nome.";

    public const string UPDATE_PRODUCER_INTEGRATION_CANCELED_NOTIFICATION_CODE = "UPDATE_PRODUCER_INTEGRATION_CANCELED";
    private const string UPDATE_PRODUCER_INTEGRATION_CANCELED_NOTIFICATION_MESSAGE = "A alteração da integração de pagamento foi cancelada.";

    public async Task<MethodResult<ProducerIntegrationOutput>> ExecuteAsync(UpdateProducerIntegrationUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = UpdateProducerIntegrationDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("UpdateProducerIntegration", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return BuildInvalidDataResult(input, validationResult);

            var producerSlug = input.ProducerSlug.Value;

            // Id fora do formato não existe: mesma resposta de uma integração de outro produtor.
            if (!Guid.TryParse(input.IntegrationId, out var integrationId))
                return BuildNotFoundResult(producerSlug);

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var producerId = await _producerRepository.GetIdBySlugAsync(producerSlug, cancellationToken);

            if (producerId is null)
                return await RejectProducerNotFoundAsync(producerSlug);

            var providersById = PaymentConversions.ToProvidersById(await _paymentProviderRepository.GetAllAsync(cancellationToken));

            // Travada até o commit: duas alterações simultâneas da mesma integração não se sobrepõem pela metade.
            var currentModel = await _producerIntegrationRepository.GetByIdForUpdateAsync(producerId.Value, integrationId, cancellationToken);

            if (currentModel is null)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                return BuildNotFoundResult(producerSlug);
            }

            var current = PaymentConversions.ToIntegration(currentModel, providersById);

            var updateResult = current.Update(
                name: input.Name,
                status: input.Status?.Value,
                credentialChanges: input.Credentials,
                now: DateTimeValueObject.Build(DateTime.UtcNow),
                correlationId: IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

            if (updateResult.IsError)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                return BuildInvalidDataResult(input, updateResult);
            }

            var updated = updateResult.Output!;

            var changedCredentials = input.Credentials
                .Where(credential => credential.Value is not null)
                .Select(credential => PaymentConversions.EncryptCredential(_credentialCipher, updated, credential.Key, credential.Value!.Value))
                .ToArray();

            var removedCredentialKeys = input.Credentials
                .Where(credential => credential.Value is null && current.CredentialKeys.Contains(credential.Key))
                .Select(credential => credential.Key)
                .ToArray();

            var credentials = currentModel.Credentials
                .Where(credential => !removedCredentialKeys.Contains(credential.Key) && changedCredentials.All(changed => changed.Key != credential.Key))
                .Concat(changedCredentials)
                .ToArray();

            var repositoryResult = await _producerIntegrationRepository.UpdateAsync(
                integration: PaymentConversions.ToModel(updated, credentials),
                changedCredentials: changedCredentials,
                removedCredentialKeys: removedCredentialKeys,
                cancellationToken: cancellationToken);

            if (repositoryResult == ProducerIntegrationUpdateResult.NameAlreadyExists)
                return await RejectNameAlreadyExistsAsync(integrationId, producerSlug);

            await _unitOfWork.CommitAsync(cancellationToken);

            return BuildSuccessResult(producerSlug, current, updated, credentials, changedCredentials, removedCredentialKeys);
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<ProducerIntegrationOutput>.Error([Notification.Error(UPDATE_PRODUCER_INTEGRATION_CANCELED_NOTIFICATION_CODE, UPDATE_PRODUCER_INTEGRATION_CANCELED_NOTIFICATION_MESSAGE)]);

            UpdateProducerIntegrationLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(UpdateProducerIntegrationDiagnostics.OUTCOME_ERROR);

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
            UpdateProducerIntegrationLog.RollbackFailed(_logger, ex);
        }
    }

    private MethodResult<ProducerIntegrationOutput> BuildSuccessResult(
        string producerSlug,
        ProducerIntegration current,
        ProducerIntegration updated,
        IReadOnlyList<ProducerIntegrationCredentialModel> credentials,
        IReadOnlyList<ProducerIntegrationCredentialModel> changedCredentials,
        IReadOnlyList<string> removedCredentialKeys)
    {
        RecordOutcome(UpdateProducerIntegrationDiagnostics.OUTCOME_UPDATED);

        Activity.Current?.SetTag("ntix.payments.integration_id", updated.Id.Value);

        UpdateProducerIntegrationLog.Updated(
            logger: _logger,
            integrationId: updated.Id.Value,
            producerSlug: producerSlug,
            providerCode: updated.Provider.Code,
            previousStatus: current.Status.ToString(),
            status: updated.Status.ToString(),
            changedCredentialKeys: string.Join(", ", changedCredentials.Select(credential => credential.Key).Order(StringComparer.Ordinal)),
            removedCredentialKeys: string.Join(", ", removedCredentialKeys.Order(StringComparer.Ordinal)));

        if (current.Status == IntegrationStatus.ACTIVE && updated.Status == IntegrationStatus.INACTIVE)
            UpdateProducerIntegrationLog.Inactivated(_logger, updated.Id.Value, producerSlug);

        return MethodResult<ProducerIntegrationOutput>.Success(PaymentConversions.ToOutput(updated, credentials, _credentialCipher));
    }

    private MethodResult<ProducerIntegrationOutput> BuildInvalidDataResult(UpdateProducerIntegrationUseCaseInput input, MethodResult validationResult)
    {
        RecordOutcome(UpdateProducerIntegrationDiagnostics.OUTCOME_VALIDATION_FAILED);

        UpdateProducerIntegrationLog.InvalidData(
            logger: _logger,
            producerSlug: input.ProducerSlug.IsValid ? input.ProducerSlug.Value : "-",
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return MethodResult<ProducerIntegrationOutput>.Error(validationResult.Notifications);
    }

    private MethodResult<ProducerIntegrationOutput> BuildNotFoundResult(string producerSlug)
    {
        RecordOutcome(UpdateProducerIntegrationDiagnostics.OUTCOME_NOT_FOUND);

        UpdateProducerIntegrationLog.NotFound(_logger, producerSlug);

        return MethodResult<ProducerIntegrationOutput>.Error([Notification.Error(INTEGRATION_NOT_FOUND_NOTIFICATION_CODE, INTEGRATION_NOT_FOUND_NOTIFICATION_MESSAGE)]);
    }

    private async Task<MethodResult<ProducerIntegrationOutput>> RejectProducerNotFoundAsync(string producerSlug)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        RecordOutcome(UpdateProducerIntegrationDiagnostics.OUTCOME_PRODUCER_NOT_FOUND);

        UpdateProducerIntegrationLog.ProducerNotFound(_logger, producerSlug);

        return MethodResult<ProducerIntegrationOutput>.Error([Notification.Error(PRODUCER_NOT_FOUND_NOTIFICATION_CODE, PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE)]);
    }

    private async Task<MethodResult<ProducerIntegrationOutput>> RejectNameAlreadyExistsAsync(Guid integrationId, string producerSlug)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        RecordOutcome(UpdateProducerIntegrationDiagnostics.OUTCOME_NAME_ALREADY_EXISTS);

        UpdateProducerIntegrationLog.NameAlreadyExists(_logger, integrationId, producerSlug);

        return MethodResult<ProducerIntegrationOutput>.Error([Notification.Error(INTEGRATION_NAME_ALREADY_EXISTS_NOTIFICATION_CODE, INTEGRATION_NAME_ALREADY_EXISTS_NOTIFICATION_MESSAGE)]);
    }
}
