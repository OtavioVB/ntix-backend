namespace Application.UseCases.Base.Customers.Outputs;

/// <summary>Resposta do login e da renovação do cliente (REQ12): o par de tokens e o primeiro nome.</summary>
public sealed record CustomerSessionOutput(
    string AccessToken,
    int AccessTokenExpiresIn,
    string RefreshToken,
    int RefreshTokenExpiresIn,
    CustomerSessionCustomerOutput Customer);

public sealed record CustomerSessionCustomerOutput(Guid Id, string FirstName);

/// <summary>Meus dados (REQ12): o próprio cadastro, com o CPF ofuscado.</summary>
public sealed record CurrentCustomerOutput(Guid Id, string FullName, string Email, string Document, string? PhoneNumber);
