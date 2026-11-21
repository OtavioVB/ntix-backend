using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.GetCustomerOrders;
using Application.UseCases.GetCustomerOrders.Inputs;
using Application.UseCases.GetCustomerTicket;
using Application.UseCases.GetCustomerTicket.Inputs;
using Application.UseCases.GetCustomerTickets;
using Application.UseCases.GetCustomerTickets.Inputs;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.CustomerLists;

/// <summary>Falhas e cancelamentos da área do comprador (REQ13): a exceção sobe ao controller e a transação é desfeita.</summary>
public sealed class CustomerListFailureTests : IDisposable
{
    private static readonly Guid CUSTOMER_ID = Guid.Parse("1d2c3b4a-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly ITicketRepository _ticketRepository = Substitute.For<ITicketRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(OrderTestData.NOW));
    private readonly AuditableInfoValueObject _auditableInfo = AuditableInfoValueObject.Build(
        ProducerSessionTestData.CORRELATION_ID, "ntix-web", CUSTOMER_ID.ToString(), ProducerSessionTestData.REQUESTED_AT);

    void IDisposable.Dispose()
        => _meterFactory.Dispose();

    [Fact]
    public async Task Given_RepositoryFailure_When_ListingOrders_Should_RollbackAndRethrow()
    {
        // Arrange
        _orderRepository.ListByCustomerAsync(CUSTOMER_ID, null, Arg.Any<DateTime>(), 0, 20, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("banco fora"));
        var useCase = new GetCustomerOrdersUseCase(_orderRepository, _producerRepository, _unitOfWork, _clock, new GetCustomerOrdersDiagnostics(_meterFactory), new CapturingLogger<GetCustomerOrdersUseCase>());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(GetCustomerOrdersUseCaseInput.Create(CUSTOMER_ID.ToString(), null, null, null), _auditableInfo));
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_RollbackAlsoFails_When_ListingOrders_Should_StillRethrowTheOriginalFailure()
    {
        // Arrange
        _orderRepository.ListByCustomerAsync(CUSTOMER_ID, null, Arg.Any<DateTime>(), 0, 20, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha original"));
        _unitOfWork.RollbackAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("rollback falhou"));
        var useCase = new GetCustomerOrdersUseCase(_orderRepository, _producerRepository, _unitOfWork, _clock, new GetCustomerOrdersDiagnostics(_meterFactory), new CapturingLogger<GetCustomerOrdersUseCase>());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(GetCustomerOrdersUseCaseInput.Create(CUSTOMER_ID.ToString(), null, null, null), _auditableInfo));
        Assert.Equal("falha original", exception.Message);
    }

    [Fact]
    public async Task Given_CustomerClaimNotAGuid_When_ListingTickets_Should_ReportCustomerNotFound()
    {
        // Arrange
        var useCase = new GetCustomerTicketsUseCase(_ticketRepository, _unitOfWork, _clock, new GetCustomerTicketsDiagnostics(_meterFactory), new CapturingLogger<GetCustomerTicketsUseCase>());

        // Act
        var result = await useCase.ExecuteAsync(GetCustomerTicketsUseCaseInput.Create("nao-e-guid", null, null, null), _auditableInfo);

        // Assert
        Assert.Equal(GetCustomerTicketsUseCase.CUSTOMER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _ticketRepository.DidNotReceiveWithAnyArgs().ListByCustomerAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task Given_CancellationRequested_When_ListingTickets_Should_ReturnCanceledNotification()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _ticketRepository.ListByCustomerAsync(CUSTOMER_ID, true, Arg.Any<DateTime>(), 0, 20, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        var useCase = new GetCustomerTicketsUseCase(_ticketRepository, _unitOfWork, _clock, new GetCustomerTicketsDiagnostics(_meterFactory), new CapturingLogger<GetCustomerTicketsUseCase>());

        // Act
        var result = await useCase.ExecuteAsync(GetCustomerTicketsUseCaseInput.Create(CUSTOMER_ID.ToString(), null, null, null), _auditableInfo, cancellation.Token);

        // Assert
        Assert.Equal(GetCustomerTicketsUseCase.GET_CUSTOMER_TICKETS_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_RepositoryFailure_When_ListingTickets_Should_RollbackAndRethrow()
    {
        // Arrange
        _ticketRepository.ListByCustomerAsync(CUSTOMER_ID, true, Arg.Any<DateTime>(), 0, 20, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("banco fora"));
        var useCase = new GetCustomerTicketsUseCase(_ticketRepository, _unitOfWork, _clock, new GetCustomerTicketsDiagnostics(_meterFactory), new CapturingLogger<GetCustomerTicketsUseCase>());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(GetCustomerTicketsUseCaseInput.Create(CUSTOMER_ID.ToString(), null, null, null), _auditableInfo));
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_CancellationRequested_When_OpeningTicket_Should_ReturnCanceledNotification()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _ticketRepository.GetByCodeForCustomerAsync("K7M2QX9HP4TA", CUSTOMER_ID, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());
        var useCase = new GetCustomerTicketUseCase(_ticketRepository, _unitOfWork, new GetCustomerTicketDiagnostics(_meterFactory), new CapturingLogger<GetCustomerTicketUseCase>());

        // Act
        var result = await useCase.ExecuteAsync(GetCustomerTicketUseCaseInput.Create("K7M2QX9HP4TA", CUSTOMER_ID.ToString()), _auditableInfo, cancellation.Token);

        // Assert
        Assert.Equal(GetCustomerTicketUseCase.GET_CUSTOMER_TICKET_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_RepositoryFailure_When_OpeningTicket_Should_RollbackAndRethrow()
    {
        // Arrange
        _ticketRepository.GetByCodeForCustomerAsync("K7M2QX9HP4TA", CUSTOMER_ID, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("banco fora"));
        var useCase = new GetCustomerTicketUseCase(_ticketRepository, _unitOfWork, new GetCustomerTicketDiagnostics(_meterFactory), new CapturingLogger<GetCustomerTicketUseCase>());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(GetCustomerTicketUseCaseInput.Create("K7M2QX9HP4TA", CUSTOMER_ID.ToString()), _auditableInfo));
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_RollbackFailsAfterTicketFailure_When_OpeningTicket_Should_StillRethrowTheOriginalFailure()
    {
        // Arrange
        _ticketRepository.GetByCodeForCustomerAsync("K7M2QX9HP4TA", CUSTOMER_ID, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha original"));
        _unitOfWork.RollbackAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("rollback falhou"));
        var useCase = new GetCustomerTicketUseCase(_ticketRepository, _unitOfWork, new GetCustomerTicketDiagnostics(_meterFactory), new CapturingLogger<GetCustomerTicketUseCase>());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(GetCustomerTicketUseCaseInput.Create("K7M2QX9HP4TA", CUSTOMER_ID.ToString()), _auditableInfo));
        Assert.Equal("falha original", exception.Message);
    }
}
