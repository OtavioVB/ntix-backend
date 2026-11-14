using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;

namespace Application.UseCases.ReplacePaymentRoute.Inputs;

public sealed record ReplacePaymentRouteUseCaseInput
{
    /// <summary>Slug da produtora na rota, já autorizado contra o do token.</summary>
    public SlugValueObject ProducerSlug { get; set; }

    public PaymentMethodValueObject PaymentMethod { get; set; }

    /// <summary>Ids das integrações em ordem de prioridade (primeiro = 1). Lista vazia remove o método.</summary>
    public IReadOnlyList<string?>? IntegrationIds { get; set; }

    private ReplacePaymentRouteUseCaseInput(SlugValueObject producerSlug, PaymentMethodValueObject paymentMethod, IReadOnlyList<string?>? integrationIds)
    {
        ProducerSlug = producerSlug;
        PaymentMethod = paymentMethod;
        IntegrationIds = integrationIds;
    }

    public static ReplacePaymentRouteUseCaseInput Create(string? producerSlug, PaymentMethodValueObject paymentMethod, IReadOnlyList<string?>? integrationIds)
        => new(SlugValueObject.Parse(producerSlug), paymentMethod, integrationIds);

    private const string INTEGRATION_IDS_REQUIRED_NOTIFICATION_CODE = "INTEGRATION_IDS_REQUIRED";
    private const string INTEGRATION_IDS_REQUIRED_NOTIFICATION_MESSAGE = "Informe a lista de integrações do método; lista vazia remove o método.";

    // Corpo sem a lista não apaga a rota por engano: para remover o método, a lista vazia tem de ser explícita.
    public MethodResult GetInputValidation()
        => MethodResult.Factory(
            ProducerSlug.MethodResult,
            PaymentMethod.MethodResult,
            IntegrationIds is null
                ? MethodResult.Error([Notification.Error(INTEGRATION_IDS_REQUIRED_NOTIFICATION_CODE, INTEGRATION_IDS_REQUIRED_NOTIFICATION_MESSAGE)])
                : MethodResult.Success());
}
