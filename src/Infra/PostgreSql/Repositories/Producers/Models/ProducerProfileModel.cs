namespace Infra.PostgreSql.Repositories.Producers.Models;

/// <summary>O que a vitrine mostra da produtora: slug e nome comercial (REQ11, RN6).</summary>
public sealed record ProducerProfileModel(Guid Id, string Slug, string Name);
