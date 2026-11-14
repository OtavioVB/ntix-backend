using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Nome que o produtor dá à integração com um fornecedor (ex.: "AbacatePay - conta principal").</summary>
public readonly struct IntegrationNameValueObject
{
    public bool IsValid { get; }

    private readonly string? _name;
    public string Value => IsValid
        ? _name!
        : throw new InvalidOperationException("It is not possible to get integration name because value object is invalid");

    public MethodResult MethodResult { get; }

    private IntegrationNameValueObject(bool isValid, MethodResult methodResult, string? name = null)
    {
        IsValid = isValid;
        _name = name;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 64;

    private const string INTEGRATION_NAME_INVALID_NOTIFICATION_CODE = "INTEGRATION_NAME_INVALID";
    private const string INTEGRATION_NAME_INVALID_NOTIFICATION_MESSAGE = "O nome da integração deve ter de 1 a 64 caracteres.";

    public static IntegrationNameValueObject Build(string? name)
    {
        var normalized = Normalize(name);

        if (normalized.Length == 0 || normalized.Length > MAX_LENGTH)
        {
            var notification = Notification.Error(
                code: INTEGRATION_NAME_INVALID_NOTIFICATION_CODE,
                message: INTEGRATION_NAME_INVALID_NOTIFICATION_MESSAGE);

            return new IntegrationNameValueObject(
                isValid: false,
                methodResult: MethodResult.Error([notification]));
        }

        return new IntegrationNameValueObject(
            isValid: true,
            name: normalized,
            methodResult: MethodResult.Success());
    }

    // Espaços repetidos viram um só: "Conta  principal" e "Conta principal" são o mesmo nome no índice único.
    private static string Normalize(string? name)
        => string.Join(' ', (name ?? string.Empty).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));

    public static implicit operator IntegrationNameValueObject(string? obj)
        => Build(obj);
}
