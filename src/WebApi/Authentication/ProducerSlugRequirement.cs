using Application.UseCases.Base.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace WebApi.Authentication;

/// <summary>Em rota com <c>{slug}</c>, só atende o usuário daquela produtora: <c>producer_slug</c> do token igual ao slug da rota.</summary>
public sealed class ProducerSlugRequirement : IAuthorizationRequirement
{
    public const string ROUTE_VALUE_KEY = "slug";
}

public sealed class ProducerSlugAuthorizationHandler : AuthorizationHandler<ProducerSlugRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ProducerSlugRequirement requirement)
    {
        var tokenSlug = context.User.FindFirst(ProducerClaims.PRODUCER_SLUG)?.Value;
        var routeSlug = (context.Resource as HttpContext)?.GetRouteValue(ProducerSlugRequirement.ROUTE_VALUE_KEY) as string;

        // Comparação exata: o slug canônico é minúsculo, e "Produtora-X" não é a mesma produtora de "produtora-x".
        if (!string.IsNullOrEmpty(tokenSlug) && string.Equals(tokenSlug, routeSlug, StringComparison.Ordinal))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
