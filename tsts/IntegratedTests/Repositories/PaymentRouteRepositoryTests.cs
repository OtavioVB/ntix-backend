using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Payments.Outputs;
using Application.UseCases.CreateProducerIntegration.Inputs;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace IntegratedTests.Repositories;

[Collection(PostgreSqlCollection.NAME)]
public sealed class PaymentRouteRepositoryTests
{
    private const string INSERT_ROUTE_SQL = """
        INSERT INTO payments.producer_payment_route (id_producer_payment_route, id_producer, payment_method, priority, id_producer_integration, updated_at, updated_correlation_id)
        VALUES (gen_random_uuid(), @id_producer, 'PIX', @priority, @id_producer_integration, now(), gen_random_uuid());
        """;

    private readonly PostgreSqlFixture _fixture;

    public PaymentRouteRepositoryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<Guid> CreateIntegrationAsync(string slug, string name)
    {
        using var scope = _fixture.Services.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<IUseCase<CreateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>>()
            .ExecuteAsync(
                CreateProducerIntegrationUseCaseInput.Create(slug, PaymentIntegratedTestData.ABACATEPAY, name, new Dictionary<string, string?> { ["API_KEY"] = "abc_dev_1234" }),
                ProducerTestData.NewAuditableInfo());

        return result.Output!.Id;
    }

    private static PaymentRouteModel Route(Guid producerId, short priority, Guid integrationId)
        => new(Guid.NewGuid(), producerId, "PIX", priority, integrationId, DateTime.UtcNow, Guid.NewGuid());

    private async Task ReplaceAsync(Guid producerId, params Guid[] integrationIds)
    {
        using var scope = _fixture.Services.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentRouteRepository>();

        await unitOfWork.BeginTransactionAsync();
        await repository.ReplaceAsync(producerId, "PIX", integrationIds.Select((id, index) => Route(producerId, (short)(index + 1), id)).ToArray());
        await unitOfWork.CommitAsync();
    }

    private async Task<IReadOnlyList<PaymentRouteModel>> GetPixRouteAsync(Guid producerId)
    {
        using var scope = _fixture.Services.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentRouteRepository>();

        await unitOfWork.BeginTransactionAsync();
        var routes = await repository.GetByProducerAndMethodAsync(producerId, "PIX");
        await unitOfWork.RollbackAsync();

        return routes;
    }

    [Fact]
    public async Task Given_TwoRoutesWithTheSamePriority_When_Inserting_Should_ViolateThePriorityIndex()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var first = await CreateIntegrationAsync(producer.Slug, "Primeira");
        var second = await CreateIntegrationAsync(producer.Slug, "Segunda");
        await _fixture.ExecuteAsync(INSERT_ROUTE_SQL, Parameters(producer.Id, 1, first));

        // Act
        var exception = await Assert.ThrowsAsync<PostgresException>(() => _fixture.ExecuteAsync(INSERT_ROUTE_SQL, Parameters(producer.Id, 1, second)));

        // Assert
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ux_producer_payment_route_priority", exception.ConstraintName);
    }

    [Fact]
    public async Task Given_TheSameIntegrationTwice_When_Inserting_Should_ViolateTheIntegrationIndex()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var integration = await CreateIntegrationAsync(producer.Slug, "Única");
        await _fixture.ExecuteAsync(INSERT_ROUTE_SQL, Parameters(producer.Id, 1, integration));

        // Act
        var exception = await Assert.ThrowsAsync<PostgresException>(() => _fixture.ExecuteAsync(INSERT_ROUTE_SQL, Parameters(producer.Id, 2, integration)));

        // Assert
        Assert.Equal("ux_producer_payment_route_integration", exception.ConstraintName);
    }

    [Fact]
    public async Task Given_IntegrationOfAnotherProducer_When_Inserting_Should_ViolateTheForeignKey()
    {
        // Arrange
        var producerX = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var producerY = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var integrationOfY = await CreateIntegrationAsync(producerY.Slug, "De Y");

        // Act
        var exception = await Assert.ThrowsAsync<PostgresException>(() => _fixture.ExecuteAsync(INSERT_ROUTE_SQL, Parameters(producerX.Id, 1, integrationOfY)));

        // Assert
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task Given_ExistingRoute_When_Replacing_Should_KeepOnlyTheNewList()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var first = await CreateIntegrationAsync(producer.Slug, "Primeira");
        var second = await CreateIntegrationAsync(producer.Slug, "Segunda");
        await ReplaceAsync(producer.Id, first, second);

        // Act
        await ReplaceAsync(producer.Id, second, first);

        // Assert
        var routes = await GetPixRouteAsync(producer.Id);
        Assert.Equal([(1, second), (2, first)], routes.Select(route => ((int)route.Priority, route.IntegrationId)));
    }

    [Fact]
    public async Task Given_ReplacementRolledBack_When_Reading_Should_KeepThePreviousRoute()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var first = await CreateIntegrationAsync(producer.Slug, "Primeira");
        var second = await CreateIntegrationAsync(producer.Slug, "Segunda");
        await ReplaceAsync(producer.Id, first);

        // Act
        using (var scope = _fixture.Services.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repository = scope.ServiceProvider.GetRequiredService<IPaymentRouteRepository>();

            await unitOfWork.BeginTransactionAsync();
            await repository.ReplaceAsync(producer.Id, "PIX", [Route(producer.Id, 1, second)]);
            await unitOfWork.RollbackAsync();
        }

        // Assert
        var route = Assert.Single(await GetPixRouteAsync(producer.Id));
        Assert.Equal(first, route.IntegrationId);
    }

    [Fact]
    public async Task Given_ConcurrentReplacements_When_Replacing_Should_KeepOneCompleteList()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var integrations = new List<Guid>();

        for (var index = 0; index < 4; index++)
            integrations.Add(await CreateIntegrationAsync(producer.Slug, $"Integração {index}"));

        Guid[][] lists = [[integrations[0], integrations[1]], [integrations[2], integrations[3], integrations[0]], [integrations[1]]];

        // Act
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => ReplaceAsync(producer.Id, lists[index % lists.Length])));

        // Assert
        var routes = await GetPixRouteAsync(producer.Id);
        Assert.Equal(Enumerable.Range(1, routes.Count), routes.Select(route => (int)route.Priority));
        Assert.Contains(lists, list => list.SequenceEqual(routes.Select(route => route.IntegrationId)));
    }

    private static NpgsqlParameter[] Parameters(Guid producerId, short priority, Guid integrationId)
        => [
            new NpgsqlParameter("@id_producer", NpgsqlDbType.Uuid) { Value = producerId },
            new NpgsqlParameter("@priority", NpgsqlDbType.Smallint) { Value = priority },
            new NpgsqlParameter("@id_producer_integration", NpgsqlDbType.Uuid) { Value = integrationId },
        ];
}
