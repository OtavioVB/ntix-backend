namespace Infra.PostgreSql.Repositories.Customers.Enums;

public enum CustomerCreationResult
{
    Created = 1,
    EmailOrDocumentAlreadyExists = 2,
    SlugAlreadyExists = 3,
}
