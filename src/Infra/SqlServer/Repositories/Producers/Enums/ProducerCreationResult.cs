namespace Infra.SqlServer.Repositories.Producers.Enums;

public enum ProducerCreationResult
{
    Created = 1,
    SlugAlreadyExists = 2,
    EmailAlreadyExists = 3,
    DocumentAlreadyExists = 4,
}
