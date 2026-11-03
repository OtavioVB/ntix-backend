namespace IntegratedTests.Support;

[CollectionDefinition(NAME)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string NAME = "PostgreSql";
}
