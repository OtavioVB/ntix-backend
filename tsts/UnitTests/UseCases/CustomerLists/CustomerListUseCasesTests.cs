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
using Infra.PostgreSql.Repositories.Producers.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.CustomerLists;

/// <summary>Casos de uso da área do comprador (REQ13): validação antes do banco, status efetivo, período e isolamento.</summary>
public sealed class CustomerListUseCasesTests : IDisposable
{
    private static readonly Guid CUSTOMER_ID = Guid.Parse("1d2c3b4a-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly Guid PRODUCER_ID = Guid.Parse("8e7f6a5b-4c3d-4e2f-9a1b-0c9d8e7f6a5b");

    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly ITicketRepository _ticketRepository = Substitute.For<ITicketRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly AuditableInfoValueObject _auditableInfo = AuditableInfoValueObject.Build(
        ProducerSessionTestData.CORRELATION_ID, "ntix-web", CUSTOMER_ID.ToString(), ProducerSessionTestData.REQUESTED_AT);

    private FakeTimeProvider Clock { get; } = new(new DateTimeOffset(OrderTestData.NOW));

    private GetCustomerOrdersUseCase OrdersUseCase()
        => new(_orderRepository, _producerRepository, _unitOfWork, Clock, new GetCustomerOrdersDiagnostics(_meterFactory), new CapturingLogger<GetCustomerOrdersUseCase>());

    private GetCustomerTicketsUseCase TicketsUseCase()
        => new(_ticketRepository, _unitOfWork, Clock, new GetCustomerTicketsDiagnostics(_meterFactory), new CapturingLogger<GetCustomerTicketsUseCase>());

    private GetCustomerTicketUseCase TicketUseCase()
        => new(_ticketRepository, _unitOfWork, new GetCustomerTicketDiagnostics(_meterFactory), new CapturingLogger<GetCustomerTicketUseCase>());

    private static CustomerOrderRowModel OrderRow(string code, string status, DateTime expiresAt)
        => new(code, status, expiresAt, OrderTestData.NOW.AddMinutes(-1), null, 240m, 0, PRODUCER_ID, "festival", "Festival", OrderTestData.NOW.AddDays(30), "Recife", "PE", null);

    void IDisposable.Dispose()
        => _meterFactory.Dispose();

    [Fact]
    public async Task Given_InvalidOrderStatus_When_ListingOrders_Should_RefuseBeforeTheDatabase()
    {
        // Arrange
        var input = GetCustomerOrdersUseCaseInput.Create(CUSTOMER_ID.ToString(), "CANCELED", null, null);

        // Act
        var result = await OrdersUseCase().ExecuteAsync(input, _auditableInfo);

        // Assert
        Assert.Equal(CustomerListFiltersValueObject.ORDER_FILTER_INVALID_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceiveWithAnyArgs().BeginTransactionAsync(default);
        await _orderRepository.DidNotReceiveWithAnyArgs().ListByCustomerAsync(default!, default, default, default, default, default);
    }

    [Fact]
    public async Task Given_PageSizeAboveFifty_When_ListingOrders_Should_RefuseWithOrderFilterCode()
    {
        // Act
        var result = await OrdersUseCase().ExecuteAsync(GetCustomerOrdersUseCaseInput.Create(CUSTOMER_ID.ToString(), null, 1, 100), _auditableInfo);

        // Assert
        Assert.Equal(CustomerListFiltersValueObject.ORDER_FILTER_INVALID_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_CustomerClaimNotAGuid_When_ListingOrders_Should_ReportCustomerNotFound()
    {
        // Act
        var result = await OrdersUseCase().ExecuteAsync(GetCustomerOrdersUseCaseInput.Create("nao-e-guid", null, null, null), _auditableInfo);

        // Assert
        Assert.Equal(GetCustomerOrdersUseCase.CUSTOMER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_ReservationPassed_When_ListingOrders_Should_ShowPendingAsExpiredAndCarryTheProducer()
    {
        // Arrange (RN3)
        var expiresAt = OrderTestData.NOW.AddMinutes(-1);
        _orderRepository.ListByCustomerAsync(CUSTOMER_ID, null, Arg.Any<DateTime>(), 0, 20, Arg.Any<CancellationToken>())
            .Returns(new CustomerOrderPageModel([OrderRow("NX7K2QH9MP", "PENDING_PAYMENT", expiresAt)], 1));
        _producerRepository.GetProfilesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new ProducerProfileModel(PRODUCER_ID, "mare-cultural", "Maré Cultural")]);

        // Act
        var result = await OrdersUseCase().ExecuteAsync(GetCustomerOrdersUseCaseInput.Create(CUSTOMER_ID.ToString(), null, null, null), _auditableInfo);

        // Assert
        var item = Assert.Single(result.Output!.Items);
        Assert.Equal("EXPIRED", item.Status);
        Assert.Equal("mare-cultural", item.Producer.Slug);
        Assert.Equal("Maré Cultural", item.Producer.Name);
        Assert.Equal(24000, item.TotalCents);
    }

    [Fact]
    public async Task Given_ListedOrders_When_Served_Should_ReturnThePageInformation()
    {
        // Arrange
        _orderRepository.ListByCustomerAsync(CUSTOMER_ID, "PAID", Arg.Any<DateTime>(), 20, 10, Arg.Any<CancellationToken>())
            .Returns(new CustomerOrderPageModel([], 25));

        // Act
        var result = await OrdersUseCase().ExecuteAsync(GetCustomerOrdersUseCaseInput.Create(CUSTOMER_ID.ToString(), "PAID", 3, 10), _auditableInfo);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Output!.Page);
        Assert.Equal(10, result.Output.PageSize);
        Assert.Equal(25, result.Output.TotalItems);
    }

    [Fact]
    public async Task Given_OperationCanceled_When_ListingOrders_Should_ReturnCanceledNotification()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _orderRepository.ListByCustomerAsync(CUSTOMER_ID, null, Arg.Any<DateTime>(), 0, 20, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await OrdersUseCase().ExecuteAsync(GetCustomerOrdersUseCaseInput.Create(CUSTOMER_ID.ToString(), null, null, null), _auditableInfo, cancellation.Token);

        // Assert
        Assert.Equal(GetCustomerOrdersUseCase.GET_CUSTOMER_ORDERS_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_UpcomingTicketsRequested_When_ListingTickets_Should_AskTheRepositoryForUpcoming()
    {
        // Arrange
        _ticketRepository.ListByCustomerAsync(CUSTOMER_ID, true, Arg.Any<DateTime>(), 0, 20, Arg.Any<CancellationToken>())
            .Returns(new CustomerTicketPageModel([], 0));

        // Act
        var result = await TicketsUseCase().ExecuteAsync(GetCustomerTicketsUseCaseInput.Create(CUSTOMER_ID.ToString(), null, null, null), _auditableInfo);

        // Assert
        Assert.True(result.IsSuccess);
        await _ticketRepository.Received(1).ListByCustomerAsync(CUSTOMER_ID, true, Arg.Any<DateTime>(), 0, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_PastTicketsRequested_When_ListingTickets_Should_AskTheRepositoryForPast()
    {
        // Arrange
        _ticketRepository.ListByCustomerAsync(CUSTOMER_ID, false, Arg.Any<DateTime>(), 0, 20, Arg.Any<CancellationToken>())
            .Returns(new CustomerTicketPageModel([], 0));

        // Act
        await TicketsUseCase().ExecuteAsync(GetCustomerTicketsUseCaseInput.Create(CUSTOMER_ID.ToString(), "PAST", null, null), _auditableInfo);

        // Assert (RN5)
        await _ticketRepository.Received(1).ListByCustomerAsync(CUSTOMER_ID, false, Arg.Any<DateTime>(), 0, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_InvalidWhen_When_ListingTickets_Should_RefuseWithTicketFilterCode()
    {
        // Act
        var result = await TicketsUseCase().ExecuteAsync(GetCustomerTicketsUseCaseInput.Create(CUSTOMER_ID.ToString(), "FUTURO", null, null), _auditableInfo);

        // Assert
        Assert.Equal(CustomerListFiltersValueObject.TICKET_FILTER_INVALID_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _ticketRepository.DidNotReceiveWithAnyArgs().ListByCustomerAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task Given_TicketOfAnotherCustomer_When_OpeningByCode_Should_ReportTicketNotFound()
    {
        // Arrange (RN1): a consulta por (code, id_customer) não devolve o ingresso alheio.
        _ticketRepository.GetByCodeForCustomerAsync("K7M2QX9HP4TA", CUSTOMER_ID, Arg.Any<CancellationToken>()).Returns((CustomerTicketRowModel?)null);

        // Act
        var result = await TicketUseCase().ExecuteAsync(GetCustomerTicketUseCaseInput.Create("K7M2QX9HP4TA", CUSTOMER_ID.ToString()), _auditableInfo);

        // Assert
        Assert.Equal(GetCustomerTicketUseCase.TICKET_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_CodeOfWrongLength_When_OpeningByCode_Should_ReportTicketNotFoundWithoutTheDatabase()
    {
        // Act
        var result = await TicketUseCase().ExecuteAsync(GetCustomerTicketUseCaseInput.Create("CURTO", CUSTOMER_ID.ToString()), _auditableInfo);

        // Assert
        Assert.Equal(GetCustomerTicketUseCase.TICKET_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _ticketRepository.DidNotReceiveWithAnyArgs().GetByCodeForCustomerAsync(default!, default, default);
    }

    [Fact]
    public async Task Given_TicketOfTheCustomer_When_OpeningByCode_Should_ReturnTheTicketWithAgeRatingAndIssueDate()
    {
        // Arrange (RN6)
        var row = new CustomerTicketRowModel(
            "K7M2QX9HP4TA", "VALID", "1º lote - Pista", "Marina Souza", "NX7K2QH9MP", OrderTestData.NOW.AddDays(-2), PRODUCER_ID,
            "festival", "Festival Horizonte", OrderTestData.NOW.AddDays(30), OrderTestData.NOW.AddDays(30).AddHours(-1), "Parque das Dunas",
            "Av. Beira-Mar, 1200", "Recife", "PE", "16", null);
        _ticketRepository.GetByCodeForCustomerAsync("K7M2QX9HP4TA", CUSTOMER_ID, Arg.Any<CancellationToken>()).Returns(row);

        // Act
        var result = await TicketUseCase().ExecuteAsync(GetCustomerTicketUseCaseInput.Create("k7m2qx9hp4ta", CUSTOMER_ID.ToString()), _auditableInfo);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("K7M2QX9HP4TA", result.Output!.Code);
        Assert.Equal("16", result.Output.AgeRating);
        Assert.Equal("Marina Souza", result.Output.HolderName);
        Assert.Equal("NX7K2QH9MP", result.Output.OrderCode);
    }
}
