using Domain.CrossCutting.MethodResultContext.Enums;
using Domain.CrossCutting.NotificationContext.Interfaces;

namespace Domain.CrossCutting.MethodResultContext;

public readonly struct MethodResult<TOutput>
{
    public bool IsSuccess => Type == EnumTypeResult.Success;
    public bool IsError => !IsSuccess;
    public EnumTypeResult Type { get; }
    public TOutput? Output { get; }
    public INotification[] Notifications { get; }

    private MethodResult(EnumTypeResult type, TOutput? output, INotification[] notifications)
    {
        Type = type;
        Output = output;
        Notifications = notifications;
    }

    public static MethodResult<TOutput> Success(TOutput output, INotification[]? notifications = null)
        => new(EnumTypeResult.Success, output, notifications ?? []);

    public static MethodResult<TOutput> Error(INotification[]? notifications = null)
        => new(EnumTypeResult.Error, default, notifications ?? []);
}
