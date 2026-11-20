using Application.UseCases.Base;
using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.Base.Payments;
using Application.UseCases.CreateOrderPayment.Inputs;
using Application.UseCases.RankPaymentIntegrations.Inputs;
using Application.UseCases.RankPaymentIntegrations.Outputs;
using Domain.BoundedContexts.OrderContext.Entities;
using Domain.BoundedContexts.OrderContext.Enums;
using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.BoundedContexts.PaymentContext.Services;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;
using Domain.ValueObjects;
using Infra.Cryptography.Credentials.Interfaces;
using Infra.Http.Payments.Interfaces;
using Infra.Http.Payments.Models;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Models;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Application.UseCases.CreateOrderPayment;

/// <summary>
/// Gera o Pix do pedido na conta do produtor (REQ7). O pedido é travado durante a operação, então um segundo clique devolve o
/// mesmo Pix (RN4). A cobrança vai pelas integrações do ranqueamento, em ordem, e qualquer falha de uma passa para a próxima (RN7).
/// A chamada ao fornecedor acontece dentro da transação, com tempo limite por tentativa; os lotes não são travados.
/// </summary>
public sealed class CreateOrderPaymentUseCase : IUseCase<CreateOrderPaymentUseCaseInput, CreateOrderPaymentOutput>
{
    private const string API_KEY_CREDENTIAL_KEY = "API_KEY";

    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IProducerIntegrationRepository _integrationRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IUseCase<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput> _rankUseCase;
    private readonly IReadOnlyList<IPixChargeClient> _pixClients;
    private readonly ICredentialCipher _cipher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly PaymentConfiguration _paymentConfiguration;
    private readonly CreateOrderPaymentDiagnostics _diagnostics;
    private readonly ILogger<CreateOrderPaymentUseCase> _logger;

    public CreateOrderPaymentUseCase(
        IOrderRepository orderRepository,
        IPaymentRepository paymentRepository,
        IProducerIntegrationRepository integrationRepository,
        ICustomerRepository customerRepository,
        IEventRepository eventRepository,
        IUseCase<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput> rankUseCase,
        IEnumerable<IPixChargeClient> pixClients,
        ICredentialCipher cipher,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        PaymentConfiguration paymentConfiguration,
        CreateOrderPaymentDiagnostics diagnostics,
        ILogger<CreateOrderPaymentUseCase> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _integrationRepository = integrationRepository;
        _customerRepository = customerRepository;
        _eventRepository = eventRepository;
        _rankUseCase = rankUseCase;
        _pixClients = pixClients.ToArray();
        _cipher = cipher;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _paymentConfiguration = paymentConfiguration;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    private const string PAYMENT_METHOD_UNAVAILABLE_NOTIFICATION_MESSAGE = "O método de pagamento não está disponível para este pedido.";

    public const string ORDER_NOT_FOUND_NOTIFICATION_CODE = "ORDER_NOT_FOUND";
    private const string ORDER_NOT_FOUND_NOTIFICATION_MESSAGE = "O pedido informado não existe.";

    public const string ORDER_ALREADY_PAID_NOTIFICATION_CODE = "ORDER_ALREADY_PAID";
    private const string ORDER_ALREADY_PAID_NOTIFICATION_MESSAGE = "Este pedido já foi pago.";

    public const string ORDER_EXPIRED_NOTIFICATION_CODE = "ORDER_EXPIRED";
    private const string ORDER_EXPIRED_NOTIFICATION_MESSAGE = "A reserva deste pedido venceu e os ingressos voltaram para a venda. Faça um novo pedido.";

    public const string ORDER_PAYMENT_WINDOW_TOO_SHORT_NOTIFICATION_CODE = "ORDER_PAYMENT_WINDOW_TOO_SHORT";
    private const string ORDER_PAYMENT_WINDOW_TOO_SHORT_NOTIFICATION_MESSAGE = "A reserva está perto do fim e não comporta um novo Pix. Faça um novo pedido.";

    public const string PAYMENT_PROVIDERS_UNAVAILABLE_NOTIFICATION_CODE = "PAYMENT_PROVIDERS_UNAVAILABLE";
    private const string PAYMENT_PROVIDERS_UNAVAILABLE_NOTIFICATION_MESSAGE = "Não foi possível gerar o Pix agora. Tente de novo em instantes.";

    public const string CREATE_ORDER_PAYMENT_CANCELED_NOTIFICATION_CODE = "CREATE_ORDER_PAYMENT_CANCELED";
    private const string CREATE_ORDER_PAYMENT_CANCELED_NOTIFICATION_MESSAGE = "A geração do Pix foi cancelada.";

    public async Task<MethodResult<CreateOrderPaymentOutput>> ExecuteAsync(CreateOrderPaymentUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = CreateOrderPaymentDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("CreateOrderPayment", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return Rejected(CreateOrderPaymentDiagnostics.OUTCOME_VALIDATION_FAILED, validationResult.Notifications);

            if (!input.Code.IsValid || input.CustomerId is null)
                return Rejected(CreateOrderPaymentDiagnostics.OUTCOME_ORDER_NOT_FOUND, [Notification.Error(ORDER_NOT_FOUND_NOTIFICATION_CODE, ORDER_NOT_FOUND_NOTIFICATION_MESSAGE)]);

            var method = input.Method.Value;
            var customerId = input.CustomerId.Value;

            // Método sem adaptador na Ntix não é oferecido, mesmo configurado pelo produtor (RN1).
            if (!PaymentMethodsWithAdapter.ALL.Contains(method))
                return Rejected(CreateOrderPaymentDiagnostics.OUTCOME_UNAVAILABLE, [Notification.Error(PaymentIntegrationRanking.PAYMENT_METHOD_UNAVAILABLE_NOTIFICATION_CODE, PAYMENT_METHOD_UNAVAILABLE_NOTIFICATION_MESSAGE)]);

            // O PostgreSQL guarda microssegundos: o instante é truncado para a resposta ser igual ao que fica gravado.
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            now = now.AddTicks(-(now.Ticks % 10));

            var correlationId = IdValueObject.Build(auditableInfo.CorrelationId.ToString());

            // Leitura sem trava para saber o produtor do pedido: o ranqueamento abre a transação dele e não pode ser aninhado.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var found = await _orderRepository.GetByCodeAsync(input.Code.Value, customerId, cancellationToken);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            if (found is null)
                return Rejected(CreateOrderPaymentDiagnostics.OUTCOME_ORDER_NOT_FOUND, [Notification.Error(ORDER_NOT_FOUND_NOTIFICATION_CODE, ORDER_NOT_FOUND_NOTIFICATION_MESSAGE)]);

            var ranking = await _rankUseCase.ExecuteAsync(RankPaymentIntegrationsUseCaseInput.Create(found.ProducerId, method.ToString()), auditableInfo, cancellationToken);

            if (ranking.IsError)
                return Rejected(CreateOrderPaymentDiagnostics.OUTCOME_UNAVAILABLE, ranking.Notifications);

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var locked = await _orderRepository.GetByCodeForUpdateAsync(input.Code.Value, customerId, cancellationToken);

            if (locked is null)
                return await RejectAsync(CreateOrderPaymentDiagnostics.OUTCOME_ORDER_NOT_FOUND, ORDER_NOT_FOUND_NOTIFICATION_CODE, ORDER_NOT_FOUND_NOTIFICATION_MESSAGE);

            var order = OrderConversions.ToOrder(locked);
            var latestModel = await _paymentRepository.GetLatestByOrderAsync(order.Id.Value, cancellationToken);
            var latest = latestModel is null ? null : OrderPaymentConversions.ToPayment(latestModel);

            var effectiveStatus = order.GetEffectiveStatus(now);

            if (effectiveStatus == OrderStatus.PAID)
                return await RejectAsync(CreateOrderPaymentDiagnostics.OUTCOME_ORDER_PAID, ORDER_ALREADY_PAID_NOTIFICATION_CODE, ORDER_ALREADY_PAID_NOTIFICATION_MESSAGE);

            // RN2: a reserva vencida é gravada agora, na primeira escrita do pedido depois do vencimento (RN12).
            if (effectiveStatus == OrderStatus.EXPIRED)
                return await ExpireAsync(order, latest, now, correlationId);

            if (latest is not null && latest.IsPendingAndValid(now))
                return await ReuseAsync(order, latest, now);

            if (latest is { Status: PaymentStatus.PENDING })
                await _paymentRepository.UpdateStatusAsync(OrderPaymentConversions.ToModel(latest.MarkExpired(now, correlationId)), cancellationToken);

            var window = PixWindow.Calculate(order.ExpiresAt.Value, TimeSpan.FromSeconds(_paymentConfiguration.ConfirmationGraceSeconds), TimeSpan.FromSeconds(_paymentConfiguration.PixMinimumSeconds), now);

            if (!window.Fits)
                return await CommitAndRejectAsync(CreateOrderPaymentDiagnostics.OUTCOME_WINDOW_TOO_SHORT, ORDER_PAYMENT_WINDOW_TOO_SHORT_NOTIFICATION_CODE, ORDER_PAYMENT_WINDOW_TOO_SHORT_NOTIFICATION_MESSAGE);

            var customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken);
            var @event = await _eventRepository.GetByIdAsync(locked.ProducerId, locked.EventId, cancellationToken);

            // Cliente e evento do pedido existem pelas chaves estrangeiras; sem eles o pedido não é pagável.
            if (customer is null || @event is null)
                return await RejectAsync(CreateOrderPaymentDiagnostics.OUTCOME_ORDER_NOT_FOUND, ORDER_NOT_FOUND_NOTIFICATION_CODE, ORDER_NOT_FOUND_NOTIFICATION_MESSAGE);

            var paymentId = IdValueObject.Build();
            var request = BuildChargeRequest(paymentId.Value, order, customer, @event.Title, window.ExpiresInSeconds);
            var integrations = await _integrationRepository.GetByProducerAsync(locked.ProducerId, cancellationToken);

            var (charge, integrationId, attempts) = await ChargeAsync(ranking.Output!.Integrations, integrations, request, order.Code.Value, cancellationToken);

            if (charge is null)
            {
                CreateOrderPaymentLog.AllFailed(_logger, order.Code.Value, attempts);

                return await CommitAndRejectAsync(CreateOrderPaymentDiagnostics.OUTCOME_PROVIDERS_FAILED, PAYMENT_PROVIDERS_UNAVAILABLE_NOTIFICATION_CODE, PAYMENT_PROVIDERS_UNAVAILABLE_NOTIFICATION_MESSAGE);
            }

            var payment = Payment.CreatePix(
                id: paymentId,
                orderId: order.Id,
                producerId: order.ProducerId,
                integrationId: IdValueObject.Build(integrationId.ToString()),
                amountCents: order.TotalCents,
                providerReference: charge.Reference!,
                pixCopyPaste: charge.CopyPasteCode!,
                pixQrCodeImage: charge.QrCodeImage!,
                expiresAt: window.ExpiresAt,
                now: now,
                correlationId: correlationId);

            await _paymentRepository.CreateAsync(OrderPaymentConversions.ToModel(payment), cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);

            RecordOutcome(CreateOrderPaymentDiagnostics.OUTCOME_CREATED);

            return MethodResult<CreateOrderPaymentOutput>.Success(new CreateOrderPaymentOutput(OrderPaymentConversions.ToOutput(payment, now), Reused: false));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<CreateOrderPaymentOutput>.Error([Notification.Error(CREATE_ORDER_PAYMENT_CANCELED_NOTIFICATION_CODE, CREATE_ORDER_PAYMENT_CANCELED_NOTIFICATION_MESSAGE)]);

            CreateOrderPaymentLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(CreateOrderPaymentDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    /// <summary>Tenta as integrações na ordem do ranqueamento, até o teto de tentativas (RN6, RN7). Devolve nulo se todas falharem.</summary>
    private async Task<(PixChargeResultModel? Charge, Guid IntegrationId, int Attempts)> ChargeAsync(
        IReadOnlyList<RankedIntegrationOutput> ranked,
        IReadOnlyList<ProducerIntegrationModel> integrations,
        PixChargeRequestModel request,
        string orderCode,
        CancellationToken cancellationToken)
    {
        var attempts = 0;

        foreach (var candidate in ranked.Take(_paymentConfiguration.MaxProviderAttempts))
        {
            attempts++;

            var watch = Stopwatch.StartNew();
            var result = await TryChargeAsync(candidate, integrations, request, cancellationToken);

            _diagnostics.ProviderCalled(candidate.ProviderCode, result.Outcome == PixChargeOutcome.Created ? CreateOrderPaymentDiagnostics.PROVIDER_CALL_CREATED : result.FailureReason!, watch.Elapsed);

            if (result.Outcome == PixChargeOutcome.Created)
            {
                CreateOrderPaymentLog.Created(_logger, request.PaymentId, orderCode, candidate.IntegrationId, candidate.ProviderCode, attempts);

                if (attempts > 1)
                    _diagnostics.FallbackUsed();

                return (result, candidate.IntegrationId, attempts);
            }

            CreateOrderPaymentLog.AttemptFailed(_logger, attempts, candidate.IntegrationId, candidate.ProviderCode, orderCode, result.FailureReason!);
        }

        return (null, Guid.Empty, attempts);
    }

    private async Task<PixChargeResultModel> TryChargeAsync(RankedIntegrationOutput candidate, IReadOnlyList<ProducerIntegrationModel> integrations, PixChargeRequestModel request, CancellationToken cancellationToken)
    {
        var client = _pixClients.FirstOrDefault(pixClient => pixClient.ProviderCode == candidate.ProviderCode);

        if (client is null)
            return PixChargeResultModel.ProviderError("no_adapter");

        var credential = integrations.FirstOrDefault(integration => integration.Id == candidate.IntegrationId)?.Credentials.FirstOrDefault(item => item.Key == API_KEY_CREDENTIAL_KEY);

        if (credential is null)
            return PixChargeResultModel.ProviderError("missing_credential");

        string apiKey;

        try
        {
            // RN10: decifrada só agora, em memória.
            apiKey = PaymentConversions.DecryptCredential(_cipher, candidate.IntegrationId, credential);
        }
        catch (CryptographicException)
        {
            return PixChargeResultModel.ProviderError("credential_unreadable");
        }

        return await client.CreatePixChargeAsync(apiKey, request, cancellationToken);
    }

    private static PixChargeRequestModel BuildChargeRequest(Guid paymentId, Order order, CustomerModel customer, string eventTitle, int expiresInSeconds)
        => new(
            PaymentId: paymentId,
            OrderCode: order.Code.Value,
            AmountCents: order.TotalCents,
            ExpiresInSeconds: expiresInSeconds,
            Description: PixChargeDescription.Build(order.Code.Value, eventTitle),
            CustomerName: customer.FullName,
            CustomerEmail: customer.Email,
            CustomerTaxId: customer.Document,
            CustomerCellphone: FormatCellphone(customer.PhoneNumber));

    // O cliente guarda só os 11 dígitos (DDD e número); o fornecedor recebe com máscara, e só quando o cliente tem celular.
    private static string? FormatCellphone(string? digits)
        => digits is { Length: 11 } ? $"({digits[..2]}) {digits[2..7]}-{digits[7..]}" : null;

    private async Task<MethodResult<CreateOrderPaymentOutput>> ReuseAsync(Order order, Payment latest, DateTime now)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        RecordOutcome(CreateOrderPaymentDiagnostics.OUTCOME_REUSED);

        CreateOrderPaymentLog.Reused(_logger, latest.Id.Value, order.Code.Value);

        return MethodResult<CreateOrderPaymentOutput>.Success(new CreateOrderPaymentOutput(OrderPaymentConversions.ToOutput(latest, now), Reused: true));
    }

    private async Task<MethodResult<CreateOrderPaymentOutput>> ExpireAsync(Order order, Payment? latest, DateTime now, IdValueObject correlationId)
    {
        await _orderRepository.UpdateStatusAsync(OrderConversions.ToModel(order.MarkExpired(now, correlationId)));

        if (latest is { Status: PaymentStatus.PENDING })
            await _paymentRepository.UpdateStatusAsync(OrderPaymentConversions.ToModel(latest.MarkExpired(now, correlationId)));

        return await CommitAndRejectAsync(CreateOrderPaymentDiagnostics.OUTCOME_ORDER_EXPIRED, ORDER_EXPIRED_NOTIFICATION_CODE, ORDER_EXPIRED_NOTIFICATION_MESSAGE);
    }

    private async Task<MethodResult<CreateOrderPaymentOutput>> RejectAsync(string outcome, string code, string message)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        return Rejected(outcome, [Notification.Error(code, message)]);
    }

    // Recusa que antes gravou algo legítimo (vencimento derivado): confirma a transação, sem gravar cobrança nova.
    private async Task<MethodResult<CreateOrderPaymentOutput>> CommitAndRejectAsync(string outcome, string code, string message)
    {
        await _unitOfWork.CommitAsync(CancellationToken.None);

        return Rejected(outcome, [Notification.Error(code, message)]);
    }

    private MethodResult<CreateOrderPaymentOutput> Rejected(string outcome, INotification[] notifications)
    {
        RecordOutcome(outcome);

        CreateOrderPaymentLog.Rejected(_logger, string.Join(", ", notifications.Select(notification => notification.Code)));

        return MethodResult<CreateOrderPaymentOutput>.Error(notifications);
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
            CreateOrderPaymentLog.RollbackFailed(_logger, ex);
        }
    }
}
