using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Customers.Outputs;
using Domain.BoundedContexts.CustomerContext.Entities;
using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Customers.Models;

namespace Application.UseCases.Base.Customers;

/// <summary>Conversões da sessão do cliente e a montagem das respostas (REQ12).</summary>
public static class CustomerSessionConversions
{
    public static CustomerSessionModel ToModel(CustomerSession session)
        => new(
            Id: session.Id.Value,
            CustomerId: session.CustomerId.Value,
            FamilyId: session.FamilyId.Value,
            RefreshTokenHash: session.RefreshTokenHash,
            ExpiresAt: session.ExpiresAt.Value,
            CreatedAt: session.CreatedAt.Value,
            CreatedCorrelationId: session.CreatedCorrelationId.Value,
            RevokedAt: session.RevokedAt?.Value,
            ReplacedById: session.ReplacedById?.Value);

    public static CustomerSession ToEntity(CustomerSessionModel model)
        => new(
            id: IdValueObject.Build(model.Id.ToString()),
            customerId: IdValueObject.Build(model.CustomerId.ToString()),
            familyId: IdValueObject.Build(model.FamilyId.ToString()),
            refreshTokenHash: model.RefreshTokenHash,
            expiresAt: DateTimeValueObject.Build(model.ExpiresAt),
            createdAt: DateTimeValueObject.Build(model.CreatedAt),
            createdCorrelationId: IdValueObject.Build(model.CreatedCorrelationId.ToString()),
            revokedAt: model.RevokedAt is null ? null : DateTimeValueObject.Build(model.RevokedAt.Value),
            replacedById: model.ReplacedById is null ? null : IdValueObject.Build(model.ReplacedById.Value.ToString()));

    /// <summary>RN10: o corpo do login traz só o primeiro nome, nunca o nome completo.</summary>
    public static CustomerSessionOutput ToOutput(CustomerAccessTokenIssuer issuer, CustomerModel customer, CustomerSession session, RefreshTokenValueObject refreshToken, DateTime now)
        => new(
            AccessToken: issuer.Issue(customer.Id, session.Id.Value, now),
            AccessTokenExpiresIn: issuer.ExpiresInSeconds,
            RefreshToken: refreshToken.Value,
            RefreshTokenExpiresIn: (int)(session.ExpiresAt.Value - now).TotalSeconds,
            Customer: new CustomerSessionCustomerOutput(Id: customer.Id, FirstName: FirstNameOf(customer.FullName)));

    /// <summary>Meus dados: o CPF sai ofuscado (RN10), no formato fixo do <see cref="CpfValueObject"/>.</summary>
    public static CurrentCustomerOutput ToCurrent(CustomerModel customer)
        => new(
            Id: customer.Id,
            FullName: customer.FullName,
            Email: customer.Email,
            Document: CpfValueObject.Build(customer.Document).GetObfuscatedValue(),
            PhoneNumber: customer.PhoneNumber);

    private static string FirstNameOf(string fullName)
        => fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullName;
}
