using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetPaymentProviders;

internal static partial class GetPaymentProvidersLog
{
    [LoggerMessage(EventId = 4101, Level = LogLevel.Information,
        Message = "Catálogo de fornecedores de pagamento consultado: {ProviderCount} fornecedores habilitados.")]
    public static partial void Served(ILogger logger, int providerCount);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Warning,
        Message = "Consulta do catálogo de fornecedores recusada por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 4103, Level = LogLevel.Error,
        Message = "Falha inesperada ao consultar o catálogo de fornecedores de pagamento.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4104, Level = LogLevel.Error,
        Message = "Falha ao encerrar a transação de leitura do catálogo de fornecedores.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
