using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetCustomerOrders;

/// <summary>Sem dado pessoal: a lista traz quantidades e o filtro, nunca nome, e-mail ou CPF do comprador (REQ13).</summary>
internal static partial class GetCustomerOrdersLog
{
    [LoggerMessage(EventId = 7301, Level = LogLevel.Information,
        Message = "Pedidos do cliente listados (filtro {Filter}), página {Page}: {Count} de {TotalItems}.")]
    public static partial void Served(ILogger logger, string filter, int page, int count, int totalItems);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Warning,
        Message = "Lista de pedidos recusada: o cliente do token não existe.")]
    public static partial void CustomerNotFound(ILogger logger);

    [LoggerMessage(EventId = 7303, Level = LogLevel.Error,
        Message = "Falha inesperada ao listar os pedidos do cliente.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7304, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação da lista de pedidos do cliente.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
