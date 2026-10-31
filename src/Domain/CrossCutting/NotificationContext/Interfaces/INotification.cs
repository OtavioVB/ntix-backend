using Domain.CrossCutting.NotificationContext.Enums;
using System.Text.Json.Serialization;

namespace Domain.CrossCutting.NotificationContext.Interfaces;

public interface INotification
{
    public string Code { get; }
    public string Message { get; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TypeNotification Type { get; }
}
