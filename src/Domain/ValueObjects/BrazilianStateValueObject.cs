using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Sigla de uma das 27 unidades federativas (RN7), em maiúsculas.</summary>
public readonly struct BrazilianStateValueObject
{
    public bool IsValid { get; }

    private readonly string? _state;
    public string Value => IsValid
        ? _state!
        : throw new InvalidOperationException("It is not possible to get state because value object is invalid");

    public MethodResult MethodResult { get; }

    private BrazilianStateValueObject(bool isValid, MethodResult methodResult, string? state = null)
    {
        IsValid = isValid;
        _state = state;
        MethodResult = methodResult;
    }

    public const int LENGTH = 2;

    private static readonly HashSet<string> STATES =
    [
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA",
        "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO",
    ];

    private const string EVENT_STATE_INVALID_NOTIFICATION_CODE = "EVENT_STATE_INVALID";
    private const string EVENT_STATE_INVALID_NOTIFICATION_MESSAGE = "A UF deve ser a sigla de uma das 27 unidades federativas.";

    public static BrazilianStateValueObject Build(string? state)
    {
        var normalized = (state ?? string.Empty).Trim().ToUpperInvariant();

        if (!STATES.Contains(normalized))
        {
            var notification = Notification.Error(
                code: EVENT_STATE_INVALID_NOTIFICATION_CODE,
                message: EVENT_STATE_INVALID_NOTIFICATION_MESSAGE);

            return new BrazilianStateValueObject(isValid: false, methodResult: MethodResult.Error([notification]));
        }

        return new BrazilianStateValueObject(isValid: true, state: normalized, methodResult: MethodResult.Success());
    }

    public static implicit operator BrazilianStateValueObject(string? obj)
        => Build(obj);
}
