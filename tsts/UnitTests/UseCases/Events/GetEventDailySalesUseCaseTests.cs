using Application.UseCases.GetEventDailySales;
using Application.UseCases.GetEventDailySales.Inputs;
using Application.UseCases.GetEventDailySales.Outputs;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.Events;

/// <summary>Vendas por dia do evento (REQ14): dias sem venda zerados, período validado, isolamento e só leitura.</summary>
public sealed class GetEventDailySalesUseCaseTests : IDisposable
{
    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IEventRepository _eventRepository = Substitute.For<IEventRepository>();
    private readonly ISalesRepository _salesRepository = Substitute.For<ISalesRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly CapturingLogger<GetEventDailySalesUseCase> _logger = new();
    private readonly GetEventDailySalesUseCase _useCase;

    public GetEventDailySalesUseCaseTests()
    {
        _producerRepository.GetIdBySlugAsync(EventTestData.PRODUCER_SLUG, Arg.Any<CancellationToken>()).Returns(EventTestData.PRODUCER_ID);
        _eventRepository.GetByIdAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, Arg.Any<CancellationToken>()).Returns(EventTestData.NewModel());

        _measurements = new MeasurementCollector(_meterFactory);

        _useCase = new GetEventDailySalesUseCase(
            _producerRepository, _eventRepository, _salesRepository, _unitOfWork, new FakeTimeProvider(EventTestData.NOW),
            new GetEventDailySalesDiagnostics(_meterFactory), _logger);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private static GetEventDailySalesUseCaseInput Input(string? from = "2026-11-01", string? to = "2026-11-03", string? eventId = null)
        => GetEventDailySalesUseCaseInput.Create(EventTestData.PRODUCER_SLUG, eventId ?? EventTestData.EVENT_ID.ToString(), from, to);

    [Fact]
    public async Task Given_SalesOnSomeDays_When_Querying_Should_ReturnEveryDayWithZeroWhereThereWasNoSale()
    {
        // Arrange
        _salesRepository.GetDailySalesAsync(EventTestData.EVENT_ID, Arg.Any<DateTime>(), Arg.Any<DateTime>(), "America/Sao_Paulo", Arg.Any<CancellationToken>())
            .Returns([new DailySalesModel(new DateOnly(2026, 11, 1), 12, 1440.00m, 9), new DailySalesModel(new DateOnly(2026, 11, 3), 1, 120.50m, 1)]);

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("2026-11-01", result.Output!.From);
        Assert.Equal("2026-11-03", result.Output.To);
        Assert.Equal(
            [new DailySalesOutput("2026-11-01", 12, 144000, 9), new DailySalesOutput("2026-11-02", 0, 0, 0), new DailySalesOutput("2026-11-03", 1, 12050, 1)],
            result.Output.Days);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        Assert.Contains(_measurements.Measurements, measurement => measurement.Outcome == GetEventDailySalesDiagnostics.OUTCOME_SERVED);
    }

    [Fact]
    public async Task Given_Period_When_Querying_Should_AskTheRepositoryForTheLocalDaysInUtc()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert: 01/11 a 03/11 em Brasília é [01/11 03h, 04/11 03h) em UTC.
        await _salesRepository.Received(1).GetDailySalesAsync(
            EventTestData.EVENT_ID,
            new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 4, 3, 0, 0, DateTimeKind.Utc),
            "America/Sao_Paulo",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_StartAfterEnd_When_Querying_Should_ReportSalesPeriodInvalidWithoutOpeningTheTransaction()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(from: "2026-11-03", to: "2026-11-01"), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("SALES_PERIOD_INVALID", Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("nao-e-um-guid")]
    [InlineData("7d3a5b1c-0000-4000-8000-000000000000")]
    public async Task Given_EventOfAnotherProducerOrMissing_When_Querying_Should_ReportEventNotFound(string eventId)
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(eventId: eventId), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetEventDailySalesUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _salesRepository.DidNotReceive().GetDailySalesAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_UnknownProducer_When_Querying_Should_ReportProducerNotFound()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(GetEventDailySalesUseCaseInput.Create("outra-produtora", EventTestData.EVENT_ID.ToString(), null, null), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetEventDailySalesUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_Cancellation_When_Querying_Should_RollbackAndReturnCanceled()
    {
        // Arrange
        _salesRepository.GetDailySalesAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetEventDailySalesUseCase.GET_EVENT_DAILY_SALES_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Querying_Should_RollbackLogAndRethrow()
    {
        // Arrange
        _salesRepository.GetDailySalesAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("falha"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo()));
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
        Assert.Contains(_logger.Logs, log => log.EventId == 14113);
    }
}
