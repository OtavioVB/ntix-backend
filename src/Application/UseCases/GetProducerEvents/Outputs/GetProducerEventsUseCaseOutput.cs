using Application.UseCases.Base.Events.Outputs;

namespace Application.UseCases.GetProducerEvents.Outputs;

/// <param name="Events">Por início crescente.</param>
public sealed record GetProducerEventsUseCaseOutput(IReadOnlyList<ProducerEventOutput> Events);
