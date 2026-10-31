using Infra.SqlServer.Base.Configuration;
using Infra.SqlServer.Base.UnitOfWork.Interfaces;
using Microsoft.Data.SqlClient;

namespace Infra.SqlServer.Base.UnitOfWork;

public sealed class SqlServerUnitOfWork : IUnitOfWork, IDisposable, IAsyncDisposable
{
    private readonly SqlServerConfiguration _configuration;

    private SqlConnection? _connection;
    private SqlTransaction? _transaction;

    public SqlServerUnitOfWork(SqlServerConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("There is already an active transaction.");

        _connection = new SqlConnection(_configuration.ConnectionString);

        await _connection.OpenAsync(cancellationToken);

        _transaction = (SqlTransaction)await _connection.BeginTransactionAsync(cancellationToken);
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

    public SqlCommand CreateCommand(string sql)
    {
        if (_transaction is null)
            throw new InvalidOperationException("There is no active transaction to execute the command.");

        return new SqlCommand(sql, _connection, _transaction);
    }

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
