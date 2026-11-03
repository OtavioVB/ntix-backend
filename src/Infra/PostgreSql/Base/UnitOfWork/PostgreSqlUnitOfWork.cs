using Infra.PostgreSql.Base.Configuration;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Npgsql;

namespace Infra.PostgreSql.Base.UnitOfWork;

public sealed class PostgreSqlUnitOfWork : IUnitOfWork, IDisposable, IAsyncDisposable
{
    private readonly PostgreSqlConfiguration _configuration;

    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;

    public PostgreSqlUnitOfWork(PostgreSqlConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("There is already an active transaction.");

        _connection = new NpgsqlConnection(_configuration.ConnectionString);

        await _connection.OpenAsync(cancellationToken);

        _transaction = await _connection.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("There is no active transaction to commit.");

        await _transaction.CommitAsync(cancellationToken);

        await DisposeAsync();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            return;

        try
        {
            await _transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await DisposeAsync();
        }
    }

    public NpgsqlCommand CreateCommand(string sql)
    {
        if (_transaction is null)
            throw new InvalidOperationException("There is no active transaction to execute the command.");

        return new NpgsqlCommand(sql, _connection, _transaction);
    }

    // No PostgreSQL, um erro invalida a transação inteira. O savepoint permite desfazer só o comando que falhou.
    public Task SaveAsync(string savepointName, CancellationToken cancellationToken = default)
        => GetActiveTransaction().SaveAsync(savepointName, cancellationToken);

    public Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken = default)
        => GetActiveTransaction().RollbackAsync(savepointName, cancellationToken);

    public Task ReleaseSavepointAsync(string savepointName, CancellationToken cancellationToken = default)
        => GetActiveTransaction().ReleaseAsync(savepointName, cancellationToken);

    private NpgsqlTransaction GetActiveTransaction()
        => _transaction ?? throw new InvalidOperationException("There is no active transaction to manage the savepoint.");

    public void Dispose()
    {
        _transaction?.Dispose();
        _connection?.Dispose();

        _transaction = null;
        _connection = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();

        if (_connection is not null)
            await _connection.DisposeAsync();

        _transaction = null;
        _connection = null;
    }
}
