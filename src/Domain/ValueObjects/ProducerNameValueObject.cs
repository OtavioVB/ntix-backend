using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;

namespace Domain.ValueObjects;

public readonly struct ProducerNameValueObject
{
    public bool IsValid { get; }

    private readonly string? _name;
    public string Value => IsValid
        ? _name!
        : throw new InvalidOperationException("It is not possible to get producer name because value object is invalid");

    public MethodResult MethodResult { get; }

    private ProducerNameValueObject(bool isValid, MethodResult methodResult, string? name = null)
    {
        IsValid = isValid;
        _name = name;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 256;

    private const string PRODUCER_NAME_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "PRODUCER_NAME_COULD_NOT_BE_EMPTY";
    private const string PRODUCER_NAME_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "O nome da produtora não pode ser vazio.";

    private const string PRODUCER_NAME_LENGTH_INVALID_NOTIFICATION_CODE = "PRODUCER_NAME_LENGTH_INVALID";
    private const string PRODUCER_NAME_LENGTH_INVALID_NOTIFICATION_MESSAGE = "O nome da produtora excede o tamanho máximo permitido.";

    public static ProducerNameValueObject Build(string? name)
    {
        const int MAX_POSSIBLE_NOTIFICATIONS = 1;
        var notifications = new List<INotification>(MAX_POSSIBLE_NOTIFICATIONS);

        var normalized = string.Join(' ', (name ?? string.Empty).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length == 0)
        {
            notifications.Add(Notification.Error(
                code: PRODUCER_NAME_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE,
                message: PRODUCER_NAME_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE));
        }

        if (normalized.Length > MAX_LENGTH)
        {
            notifications.Add(Notification.Error(
                code: PRODUCER_NAME_LENGTH_INVALID_NOTIFICATION_CODE,
                message: PRODUCER_NAME_LENGTH_INVALID_NOTIFICATION_MESSAGE));
        }

        if (notifications.HasAnyNotifications())
        {
            return new ProducerNameValueObject(
                isValid: false,
                methodResult: MethodResult.Error(notifications.ToArray()));
        }

        return new ProducerNameValueObject(
            isValid: true,
            name: normalized,
            methodResult: MethodResult.Success());
    }

    public static implicit operator ProducerNameValueObject(string? obj)
        => Build(obj);
}
