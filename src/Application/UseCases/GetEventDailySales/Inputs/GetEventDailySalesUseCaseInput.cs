using Application.UseCases.Base.Events;
using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.GetEventDailySales.Inputs;

public sealed record GetEventDailySalesUseCaseInput
{
    /// <summary>Slug da produtora na rota, já autorizado contra o do token.</summary>
    public SlugValueObject ProducerSlug { get; set; }

    /// <summary>Nulo quando o id da rota não é um GUID: tratado como evento inexistente.</summary>
    public Guid? EventId { get; set; }

    /// <summary>Datas brutas da consulta; o período depende de "hoje", então o caso de uso o monta com o relógio.</summary>
    public string? From { get; set; }

    public string? To { get; set; }

    private GetEventDailySalesUseCaseInput(SlugValueObject producerSlug, Guid? eventId, string? from, string? to)
    {
        ProducerSlug = producerSlug;
        EventId = eventId;
        From = from;
        To = to;
    }

    public static GetEventDailySalesUseCaseInput Create(string? producerSlug, string? eventId, string? from, string? to)
        => new(SlugValueObject.Parse(producerSlug), EventConversions.ParseEventId(eventId), from, to);

    public SalesReportPeriodValueObject BuildPeriod(DateTime nowUtc)
        => SalesReportPeriodValueObject.Build(From, To, nowUtc);

    public MethodResult GetInputValidation(SalesReportPeriodValueObject period)
        => MethodResult.Factory(ProducerSlug.MethodResult, period.MethodResult);
}
