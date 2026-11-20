using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.SearchPublicEvents.Inputs;

public sealed record SearchPublicEventsUseCaseInput
{
    public EventSearchFilterValueObject Filter { get; set; }

    private SearchPublicEventsUseCaseInput(EventSearchFilterValueObject filter)
    {
        Filter = filter;
    }

    public static SearchPublicEventsUseCaseInput Create(string? producer, string? category, string? city, string? state, string? startsFrom, string? startsTo, int? page, int? pageSize)
        => new(EventSearchFilterValueObject.Build(producer, category, city, state, startsFrom, startsTo, page, pageSize));

    public MethodResult GetInputValidation()
        => Filter.MethodResult;
}
