using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Producers.Outputs;
using Domain.BoundedContexts.ProducerContext.Entities;
using Domain.BoundedContexts.ProducerContext.Enums;
using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Producers.Models;

namespace Application.UseCases.Base.Producers;

/// <summary>Conversões entre a sessão do usuário do produtor e o modelo do banco, e a montagem da resposta (REQ5).</summary>
public static class ProducerSessionConversions
{
    public static ProducerUserSessionModel ToModel(ProducerUserSession session)
        => new(
            Id: session.Id.Value,
            ProducerUserId: session.ProducerUserId.Value,
            FamilyId: session.FamilyId.Value,
            RefreshTokenHash: session.RefreshTokenHash,
            ExpiresAt: session.ExpiresAt.Value,
            CreatedAt: session.CreatedAt.Value,
            CreatedCorrelationId: session.CreatedCorrelationId.Value,
            RevokedAt: session.RevokedAt?.Value,
            ReplacedById: session.ReplacedById?.Value);

    public static ProducerUserSession ToEntity(ProducerUserSessionModel model)
        => new(
            id: IdValueObject.Build(model.Id.ToString()),
            producerUserId: IdValueObject.Build(model.ProducerUserId.ToString()),
            familyId: IdValueObject.Build(model.FamilyId.ToString()),
            refreshTokenHash: model.RefreshTokenHash,
            expiresAt: DateTimeValueObject.Build(model.ExpiresAt),
            createdAt: DateTimeValueObject.Build(model.CreatedAt),
            createdCorrelationId: IdValueObject.Build(model.CreatedCorrelationId.ToString()),
            revokedAt: model.RevokedAt is null ? null : DateTimeValueObject.Build(model.RevokedAt.Value),
            replacedById: model.ReplacedById is null ? null : IdValueObject.Build(model.ReplacedById.Value.ToString()));

    public static ProducerUserStatus GetStatus(ProducerUserModel user)
        => Enum.Parse<ProducerUserStatus>(user.Status);

    /// <summary>O access token reflete o slug e o status atuais (RN8), lidos na mesma operação.</summary>
    public static ProducerSessionOutput ToOutput(
        ProducerAccessTokenIssuer accessTokenIssuer,
        ProducerUserModel user,
        ProducerModel producer,
        ProducerUserSession session,
        RefreshTokenValueObject refreshToken,
        DateTime now)
        => new(
            AccessToken: accessTokenIssuer.Issue(
                producerUserId: user.Id,
                producerId: producer.Id,
                producerSlug: producer.Slug,
                userType: user.UserType,
                sessionId: session.Id.Value,
                now: now),
            AccessTokenExpiresIn: accessTokenIssuer.ExpiresInSeconds,
            RefreshToken: refreshToken.Value,
            RefreshTokenExpiresIn: (int)(session.ExpiresAt.Value - now).TotalSeconds,
            User: new ProducerSessionUserOutput(Id: user.Id, Name: user.Name, UserType: user.UserType, Status: user.Status),
            Producer: new ProducerSessionProducerOutput(Id: producer.Id, Slug: producer.Slug, Name: producer.Name, Status: producer.Status));
}
