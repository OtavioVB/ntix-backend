using Application.UseCases.Base.Configuration;
using Application.UseCases.CreateOrder;
using Application.UseCases.CreateOrder.Inputs;
using Domain.BoundedContexts.OrderContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Infra.PostgreSql.Repositories.Events.Enums;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Events.Models;
using Infra.PostgreSql.Repositories.Orders.Enums;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using NSubstitute;
using UnitTests.Support;

namespace UnitTests.UseCases.Orders;

/// <summary>
/// Controle de concorrência da reserva (REQ6, RN8) na orquestração do caso de uso. Um "banco" em memória imita o <c>FOR UPDATE</c>:
/// a trava do lote só é solta no commit ou no rollback da transação. Com ela, a reserva nunca passa do estoque; sem ela (controle
/// negativo), a mesma simulação vende acima do estoque, o que prova que o teste enxerga a falha.
/// </summary>
public sealed class StockConcurrencyTests
{
    private const int CONTENDERS = 20;

    /// <summary>O lote do "banco": a quantidade, o que já foi confirmado como reservado e a trava da linha.</summary>
    private sealed class StockWorld(int quantity)
    {
        public int Quantity { get; } = quantity;
        public SemaphoreSlim BatchLock { get; } = new(1, 1);
        public int CommittedReserved;
    }

    private sealed class FakeUnitOfWork(StockWorld world) : IUnitOfWork
    {
        private bool _holdsLock;
        private int _pendingReserved;

        public void HoldLock() => _holdsLock = true;

        public void AddPending(int quantity) => _pendingReserved += quantity;

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Add(ref world.CommittedReserved, _pendingReserved);
            Release();

            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            Release();

            return Task.CompletedTask;
        }

        private void Release()
        {
            _pendingReserved = 0;

            if (!_holdsLock)
                return;

            _holdsLock = false;
            world.BatchLock.Release();
        }
    }

    /// <param name="useLock">Falso é o controle negativo: ninguém trava o lote antes de somar.</param>
    /// <param name="afterRead">Gancho depois da leitura do reservado: no controle negativo segura todos até todos lerem.</param>
    private sealed class FakeBatchRepository(StockWorld world, FakeUnitOfWork unitOfWork, bool useLock, Func<Task>? afterRead) : ITicketBatchRepository
    {
        public async Task<IReadOnlyList<TicketBatchModel>> ListByIdsForUpdateAsync(Guid eventId, IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken = default)
        {
            if (useLock)
            {
                await world.BatchLock.WaitAsync(cancellationToken);
                unitOfWork.HoldLock();
            }

            return [TicketBatchTestData.NewModel(TicketBatchTestData.Details(quantity: world.Quantity))];
        }

        public async Task<IReadOnlyDictionary<Guid, TicketBatchSalesModel>> GetSalesAsync(IReadOnlyCollection<Guid> batchIds, DateTime now, CancellationToken cancellationToken = default)
        {
            var reserved = Volatile.Read(ref world.CommittedReserved);

            if (afterRead is not null)
                await afterRead();

            return new Dictionary<Guid, TicketBatchSalesModel> { [TicketBatchTestData.BATCH_ID] = new(Sold: 0, Reserved: reserved) };
        }

        public Task<TicketBatchWriteResult> CreateAsync(TicketBatchModel batch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TicketBatchModel>> ListByEventAsync(Guid producerId, Guid eventId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TicketBatchModel?> GetByIdForUpdateAsync(Guid producerId, Guid eventId, Guid batchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TicketBatchModel>> ListByEventIdsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TicketBatchWriteResult> UpdateAsync(TicketBatchModel batch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeOrderRepository(FakeUnitOfWork unitOfWork) : IOrderRepository
    {
        public Task<OrderCreationResult> CreateAsync(OrderModel order, CancellationToken cancellationToken = default)
        {
            unitOfWork.AddPending(order.Items.Sum(item => item.Quantity));

            return Task.FromResult(OrderCreationResult.Created);
        }

        public Task<OrderModel?> GetByCodeAsync(string code, Guid customerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrderModel?> GetByCodeForUpdateAsync(string code, Guid customerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrderModel?> GetByIdForUpdateAsync(Guid orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateStatusAsync(OrderModel order, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static async Task<MethodResult<Application.UseCases.Base.Orders.Outputs.OrderOutput>[]> ReserveConcurrentlyAsync(StockWorld world, int contenders, int quantityEach, bool useLock)
    {
        var eventRepository = Substitute.For<IEventRepository>();
        var producerCustomerRepository = Substitute.For<IProducerCustomerRepository>();

        eventRepository.GetPublishedBySlugAsync(EventTestData.SLUG, Arg.Any<CancellationToken>()).Returns(OrderTestData.PublishedEventModel());
        producerCustomerRepository.LinkAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var arrived = 0;
        var everyoneRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Sem a trava, todos leem o reservado antes de qualquer um gravar: é o pior caso da corrida, de forma determinística.
        Func<Task>? afterRead = useLock
            ? null
            : async () =>
            {
                if (Interlocked.Increment(ref arrived) == contenders)
                    everyoneRead.SetResult();

                await everyoneRead.Task;
            };

        using var meterFactory = new TestMeterFactory();

        var tasks = Enumerable.Range(0, contenders).Select(index => Task.Run(() =>
        {
            var unitOfWork = new FakeUnitOfWork(world);
            var useCase = new CreateOrderUseCase(
                eventRepository, new FakeBatchRepository(world, unitOfWork, useLock, afterRead), new FakeOrderRepository(unitOfWork), producerCustomerRepository, unitOfWork,
                new FakeTimeProvider(new DateTimeOffset(OrderTestData.NOW)), new OrderConfiguration(), new CreateOrderDiagnostics(meterFactory), new CapturingLogger<CreateOrderUseCase>());
            var input = CreateOrderUseCaseInput.Create(Guid.NewGuid().ToString(), EventTestData.SLUG, "HUB", [(TicketBatchTestData.BATCH_ID.ToString(), quantityEach)]);

            return useCase.ExecuteAsync(input, EventTestData.AuditableInfo());
        })).ToArray();

        return await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Given_ManyBuyersForFewTickets_When_ReservingAtTheSameTime_Should_ReserveExactlyTheStock()
    {
        // Arrange
        var world = new StockWorld(quantity: 5);

        // Act
        var results = await ReserveConcurrentlyAsync(world, CONTENDERS, quantityEach: 1, useLock: true);

        // Assert
        Assert.Equal(5, results.Count(result => result.IsSuccess));
        Assert.Equal(5, world.CommittedReserved);
        Assert.All(results.Where(result => result.IsError), result => Assert.Equal(Order.BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code));
    }

    [Fact]
    public async Task Given_OrdersOfSeveralTickets_When_ReservingAtTheSameTime_Should_NeverPassTheStock()
    {
        // Arrange: 10 compradores pedem 2 cada um para 5 ingressos: só 2 pedidos cabem (4 ingressos), e o quinto fica sobrando.
        var world = new StockWorld(quantity: 5);

        // Act
        var results = await ReserveConcurrentlyAsync(world, 10, quantityEach: 2, useLock: true);

        // Assert
        Assert.Equal(2, results.Count(result => result.IsSuccess));
        Assert.Equal(4, world.CommittedReserved);
        Assert.True(world.CommittedReserved <= world.Quantity);
    }

    [Fact]
    public async Task Given_EnoughStock_When_ReservingAtTheSameTime_Should_ServeEveryBuyer()
    {
        // Arrange
        var world = new StockWorld(quantity: 100);

        // Act
        var results = await ReserveConcurrentlyAsync(world, CONTENDERS, quantityEach: 3, useLock: true);

        // Assert
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(CONTENDERS * 3, world.CommittedReserved);
    }

    [Fact]
    public async Task Given_NoLockOnTheBatch_When_ReservingAtTheSameTime_Should_OverSell_ProvingTheLockIsWhatProtects()
    {
        // Arrange: o mesmo cenário, sem a trava do lote (controle negativo).
        var world = new StockWorld(quantity: 5);

        // Act
        var results = await ReserveConcurrentlyAsync(world, CONTENDERS, quantityEach: 1, useLock: false);

        // Assert: todos leram zero reservado e todos passaram.
        Assert.Equal(CONTENDERS, results.Count(result => result.IsSuccess));
        Assert.True(world.CommittedReserved > world.Quantity);
    }

    [Fact]
    public async Task Given_TheLockIsHeldByAnotherTransaction_When_Reserving_Should_WaitForItsEnd()
    {
        // Arrange: outra transação segura a trava do lote.
        var world = new StockWorld(quantity: 5);

        await world.BatchLock.WaitAsync();

        // Act
        var pending = ReserveConcurrentlyAsync(world, contenders: 1, quantityEach: 1, useLock: true);

        await Task.Delay(150);

        // Assert: sem a trava solta, a reserva não avança.
        Assert.False(pending.IsCompleted);

        world.BatchLock.Release();

        Assert.True((await pending).Single().IsSuccess);
    }

    [Fact]
    public async Task Given_ARefusedReservation_When_ItEnds_Should_ReleaseTheLockForTheNextBuyer()
    {
        // Arrange: estoque de 1 e dois pedidos de 2: o primeiro é recusado (rollback) e não pode prender a trava.
        var world = new StockWorld(quantity: 1);

        // Act
        var results = await ReserveConcurrentlyAsync(world, contenders: 2, quantityEach: 2, useLock: true);

        // Assert
        Assert.All(results, result => Assert.True(result.IsError));
        Assert.Equal(1, world.BatchLock.CurrentCount);
        Assert.Equal(0, world.CommittedReserved);
    }
}
