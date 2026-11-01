using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.Base.Interfaces;

public interface IUseCase<TInput, TOutput>
{
    public Task<MethodResult<TOutput>> ExecuteAsync(TInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default);
}
