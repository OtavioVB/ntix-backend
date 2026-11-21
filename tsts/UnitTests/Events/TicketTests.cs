using Domain.BoundedContexts.OrderContext.Entities;
using Domain.BoundedContexts.OrderContext.Enums;
using Domain.ValueObjects;
using UnitTests.Support;

namespace UnitTests.Events;

/// <summary>Ingresso (REQ8, RN7): a troca de código depois de uma colisão mantém o resto do ingresso.</summary>
public sealed class TicketTests
{
    [Fact]
    public void Given_TicketWithCollidingCode_When_RegeneratingCode_Should_KeepEveryOtherField()
    {
        // Arrange
        var ticket = new Ticket(
            Id: IdValueObject.Build(),
            OrderItemId: IdValueObject.Build(),
            OrderId: IdValueObject.Build(),
            CustomerId: IdValueObject.Build(),
            EventId: IdValueObject.Build(),
            Code: "AAAAAAAAAAAA",
            HolderName: "Marina Souza",
            Status: TicketStatus.VALID,
            CreatedAt: OrderTestData.NOW,
            CreatedCorrelationId: OrderTestData.CORRELATION_ID);

        // Act
        var regenerated = ticket.WithNewCode();

        // Assert
        Assert.NotEqual(ticket.Code, regenerated.Code);
        Assert.Equal(12, regenerated.Code.Length);
        Assert.Equal(ticket.Id, regenerated.Id);
        Assert.Equal("Marina Souza", regenerated.HolderName);
        Assert.Equal(TicketStatus.VALID, regenerated.Status);
    }
}
