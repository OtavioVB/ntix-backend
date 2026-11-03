namespace Infra.PostgreSql.Repositories.Producers.Enums;

public enum ProducerCreationResult
{
    Created = 1,
    EmailAlreadyExists = 2,
    SlugAlreadyExists = 3,
}
