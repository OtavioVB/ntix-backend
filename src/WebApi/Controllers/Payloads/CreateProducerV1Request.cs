namespace WebApi.Controllers.Payloads;

/// <param name="Email">E-mail da produtora e, também, de acesso do administrador.</param>
public sealed record CreateProducerV1Request(string Name, string Email, string Phone, string AdministratorName, string AdministratorPassword);
