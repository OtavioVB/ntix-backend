using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Nome do lote (REQ10, RN2): de 1 a 64 caracteres, com espaços repetidos reduzidos a um (ex.: "1º lote - Pista").</summary>
public readonly struct BatchNameValueObject
{
    public bool IsValid { get; }

    private readonly string? _name;
    public string Value => IsValid
        ? _name!
        : throw new InvalidOperationException("It is not possible to get batch name because value object is invalid");

    public MethodResult MethodResult { get; }

    private BatchNameValueObject(bool isValid, MethodResult methodResult, string? name = null)
    {
        IsValid = isValid;
        _name = name;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 64;

    private const string BATCH_NAME_INVALID_NOTIFICATION_CODE = "BATCH_NAME_INVALID";
    private const string BATCH_NAME_INVALID_NOTIFICATION_MESSAGE = "O nome do lote deve ter de 1 a 64 caracteres.";

    public static BatchNameValueObject Build(string? name)
    {
        var normalized = string.Join(' ', (name ?? string.Empty).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length == 0 || normalized.Length > MAX_LENGTH)
        {
            var notification = Notification.Error(BATCH_NAME_INVALID_NOTIFICATION_CODE, BATCH_NAME_INVALID_NOTIFICATION_MESSAGE);

            return new BatchNameValueObject(isValid: false, methodResult: MethodResult.Error([notification]));
        }

        return new BatchNameValueObject(isValid: true, name: normalized, methodResult: MethodResult.Success());
    }

    public static implicit operator BatchNameValueObject(string? obj)
        => Build(obj);
}
