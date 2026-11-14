namespace Application.UseCases.Base.Producers.Outputs;

/// <summary>Resposta do login e da renovação (REQ5): o par de tokens e quem é o usuário, com a produtora dele.</summary>
public sealed record ProducerSessionOutput(
    string AccessToken,
    int AccessTokenExpiresIn,
    string RefreshToken,
    int RefreshTokenExpiresIn,
    ProducerSessionUserOutput User,
    ProducerSessionProducerOutput Producer);

public sealed record ProducerSessionUserOutput(Guid Id, string Name, string UserType, string Status);

public sealed record ProducerSessionProducerOutput(Guid Id, string Slug, string Name, string Status);
