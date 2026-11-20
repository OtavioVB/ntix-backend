using Application.UseCases.Base.Events.Outputs;

namespace Application.UseCases.GetTicketBatches.Outputs;

/// <param name="Batches">Por início da venda e preço.</param>
public sealed record GetTicketBatchesUseCaseOutput(IReadOnlyList<TicketBatchOutput> Batches);
