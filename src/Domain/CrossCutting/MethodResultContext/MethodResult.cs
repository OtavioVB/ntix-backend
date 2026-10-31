using Domain.CrossCutting.MethodResultContext.Enums;
using Domain.CrossCutting.NotificationContext.Interfaces;

namespace Domain.CrossCutting.MethodResultContext;

public readonly struct MethodResult
{
    public bool IsSuccess => Type == EnumTypeResult.Success;
    public bool IsError => !IsSuccess;
    public EnumTypeResult Type { get; }
    public INotification[] Notifications { get; }

    private MethodResult(EnumTypeResult type, INotification[] notifications)
    {
        Type = type;
        Notifications = notifications;
    }

    public static MethodResult Error(INotification[]? notifications = null)
        => Factory(EnumTypeResult.Error, notifications);

    public static MethodResult Success(INotification[]? notifications = null)
        => Factory(EnumTypeResult.Success, notifications);

    public static MethodResult Factory(EnumTypeResult type, INotification[]? notifications = null)
        => new(type, notifications ?? []);

    public static MethodResult Factory(params MethodResult[] methodResults)
    {
        var type = EnumTypeResult.Success;
        var notifications = new List<INotification>();

        foreach (var methodResult in methodResults)
        {
            if (methodResult.IsError)
                type = EnumTypeResult.Error;

            if (methodResult.Notifications is not null)
                notifications.AddRange(methodResult.Notifications);
        }

        return new MethodResult(type, notifications.ToArray());
    }
}
