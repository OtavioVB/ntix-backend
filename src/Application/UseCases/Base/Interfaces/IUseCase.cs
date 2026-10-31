using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.Base.Interfaces;

public interface IUseCase<TInput>
{
    public Task<MethodResult> ExecuteAsync(TInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default);
}
