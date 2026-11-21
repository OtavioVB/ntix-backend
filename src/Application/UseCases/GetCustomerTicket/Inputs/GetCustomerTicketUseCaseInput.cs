namespace Application.UseCases.GetCustomerTicket.Inputs;

/// <summary>Detalhe de um ingresso do comprador (REQ13). Código vazio ou acima do tamanho do código é ingresso inexistente.</summary>
public sealed record GetCustomerTicketUseCaseInput
{
    public const int CODE_LENGTH = 12;

    public string Code { get; }

    public Guid CustomerId { get; }

    public bool IsValid { get; }

    private GetCustomerTicketUseCaseInput(string code, Guid customerId, bool isValid)
    {
        Code = code;
        CustomerId = customerId;
        IsValid = isValid;
    }

    public static GetCustomerTicketUseCaseInput Create(string? code, string? customerId)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        var validCustomer = Guid.TryParse(customerId, out var parsed);
        var validCode = normalized.Length == CODE_LENGTH;

        return new(normalized, validCustomer ? parsed : Guid.Empty, validCode && validCustomer);
    }
}
