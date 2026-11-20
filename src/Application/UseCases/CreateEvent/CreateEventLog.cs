using Microsoft.Extensions.Logging;

namespace Application.UseCases.CreateEvent;

internal static partial class CreateEventLog
{
    [LoggerMessage(EventId = 9101, Level = LogLevel.Information,
        Message = "Evento {EventId} criado em DRAFT com o slug {EventSlug} para a produtora {ProducerSlug}.")]
    public static partial void Created(ILogger logger, Guid eventId, string eventSlug, string producerSlug);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Warning,
        Message = "Criação de evento recusada para a produtora {ProducerSlug} por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string producerSlug, string notificationCodes);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Warning,
        Message = "Criação de evento recusada: a produtora {ProducerSlug} não existe.")]
    public static partial void ProducerNotFound(ILogger logger, string producerSlug);

    [LoggerMessage(EventId = 9104, Level = LogLevel.Information,
        Message = "Slug de evento {EventSlug} já usado: gerando outra versão.")]
    public static partial void SlugTaken(ILogger logger, string eventSlug);

    [LoggerMessage(EventId = 9105, Level = LogLevel.Error,
        Message = "Falha inesperada ao criar um evento.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9106, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação da criação de evento.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
