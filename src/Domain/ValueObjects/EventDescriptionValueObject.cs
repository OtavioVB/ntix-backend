using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Descrição do evento (RN2): opcional, até 5000 caracteres. Vazia vira ausente.</summary>
public readonly struct EventDescriptionValueObject
{
    public bool IsValid { get; }

    private readonly string? _description;

    /// <summary>Nula quando o produtor não informou descrição.</summary>
    public string? Value => IsValid
        ? _description
        : throw new InvalidOperationException("It is not possible to get event description because value object is invalid");

    public MethodResult MethodResult { get; }

    private EventDescriptionValueObject(bool isValid, MethodResult methodResult, string? description = null)
    {
        IsValid = isValid;
        _description = description;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 5000;

    private const string EVENT_DESCRIPTION_INVALID_NOTIFICATION_CODE = "EVENT_DESCRIPTION_INVALID";
    private const string EVENT_DESCRIPTION_INVALID_NOTIFICATION_MESSAGE = "A descrição do evento deve ter até 5000 caracteres.";

    public static EventDescriptionValueObject Build(string? description)
    {
        var trimmed = description?.Trim();

        if (string.IsNullOrEmpty(trimmed))
            return new EventDescriptionValueObject(isValid: true, methodResult: MethodResult.Success());

        if (trimmed.Length > MAX_LENGTH)
        {
            var notification = Notification.Error(
                code: EVENT_DESCRIPTION_INVALID_NOTIFICATION_CODE,
                message: EVENT_DESCRIPTION_INVALID_NOTIFICATION_MESSAGE);

            return new EventDescriptionValueObject(isValid: false, methodResult: MethodResult.Error([notification]));
        }

        return new EventDescriptionValueObject(isValid: true, description: trimmed, methodResult: MethodResult.Success());
    }

    public static implicit operator EventDescriptionValueObject(string? obj)
        => Build(obj);
}
