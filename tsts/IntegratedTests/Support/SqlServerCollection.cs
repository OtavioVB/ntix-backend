namespace IntegratedTests.Support;

[CollectionDefinition(NAME)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string NAME = "SqlServer";
}
