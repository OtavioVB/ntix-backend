using Domain.CrossCutting.MethodResultContext.Enums;
using Domain.CrossCutting.NotificationContext.Interfaces;

namespace Domain.CrossCutting.MethodResultContext;

/// <summary>Resultado com saída, para casos de uso de consulta. A saída só existe no sucesso.</summary>
public readonly struct MethodResult<TOutput>
{
    public bool IsSuccess => Type == EnumTypeResult.Success;
    public bool IsError => !IsSuccess;
    public EnumTypeResult Type { get; }
    public INotification[] Notifications { get; }
    public TOutput? Output { get; }

    private MethodResult(EnumTypeResult type, INotification[] notifications, TOutput? output)
    {
        Type = type;
        Notifications = notifications;
        Output = output;
    }

    public static MethodResult<TOutput> Success(TOutput output, INotification[]? notifications = null)
        => new(EnumTypeResult.Success, notifications ?? [], output);

    public static MethodResult<TOutput> Error(INotification[]? notifications = null)
        => new(EnumTypeResult.Error, notifications ?? [], default);

    public static implicit operator MethodResult(MethodResult<TOutput> methodResult)
        => MethodResult.Factory(methodResult.Type, methodResult.Notifications);
}
