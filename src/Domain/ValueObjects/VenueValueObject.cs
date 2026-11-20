using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>
/// Local do evento (RN7): nome (até 120), endereço (até 256) e cidade (até 80), todos obrigatórios, e a UF.
/// Uma notificação por campo inválido, para o formulário apontar onde corrigir.
/// </summary>
public readonly struct VenueValueObject
{
    public bool IsValid { get; }

    private readonly string? _name;
    private readonly string? _address;
    private readonly string? _city;
    private readonly BrazilianStateValueObject _state;

    public string Name => IsValid ? _name! : throw InvalidValueObject();
    public string Address => IsValid ? _address! : throw InvalidValueObject();
    public string City => IsValid ? _city! : throw InvalidValueObject();
    public string State => IsValid ? _state.Value : throw InvalidValueObject();

    public MethodResult MethodResult { get; }

    private VenueValueObject(bool isValid, MethodResult methodResult, string? name = null, string? address = null, string? city = null, BrazilianStateValueObject state = default)
    {
        IsValid = isValid;
        _name = name;
        _address = address;
        _city = city;
        _state = state;
        MethodResult = methodResult;
    }

    public const int NAME_MAX_LENGTH = 120;
    public const int ADDRESS_MAX_LENGTH = 256;
    public const int CITY_MAX_LENGTH = 80;

    private const string EVENT_VENUE_NAME_INVALID_NOTIFICATION_CODE = "EVENT_VENUE_NAME_INVALID";
    private const string EVENT_VENUE_NAME_INVALID_NOTIFICATION_MESSAGE = "O nome do local deve ter de 1 a 120 caracteres.";

    private const string EVENT_VENUE_ADDRESS_INVALID_NOTIFICATION_CODE = "EVENT_VENUE_ADDRESS_INVALID";
    private const string EVENT_VENUE_ADDRESS_INVALID_NOTIFICATION_MESSAGE = "O endereço do local deve ter de 1 a 256 caracteres.";

    private const string EVENT_VENUE_CITY_INVALID_NOTIFICATION_CODE = "EVENT_VENUE_CITY_INVALID";
    private const string EVENT_VENUE_CITY_INVALID_NOTIFICATION_MESSAGE = "A cidade deve ter de 1 a 80 caracteres.";

    public static VenueValueObject Build(string? name, string? address, string? city, string? state)
    {
        var normalizedName = Normalize(name);
        var normalizedAddress = Normalize(address);
        var normalizedCity = Normalize(city);
        var stateValueObject = BrazilianStateValueObject.Build(state);

        var notifications = new List<Notification>();

        if (normalizedName.Length == 0 || normalizedName.Length > NAME_MAX_LENGTH)
            notifications.Add(Notification.Error(EVENT_VENUE_NAME_INVALID_NOTIFICATION_CODE, EVENT_VENUE_NAME_INVALID_NOTIFICATION_MESSAGE));

        if (normalizedAddress.Length == 0 || normalizedAddress.Length > ADDRESS_MAX_LENGTH)
            notifications.Add(Notification.Error(EVENT_VENUE_ADDRESS_INVALID_NOTIFICATION_CODE, EVENT_VENUE_ADDRESS_INVALID_NOTIFICATION_MESSAGE));

        if (normalizedCity.Length == 0 || normalizedCity.Length > CITY_MAX_LENGTH)
            notifications.Add(Notification.Error(EVENT_VENUE_CITY_INVALID_NOTIFICATION_CODE, EVENT_VENUE_CITY_INVALID_NOTIFICATION_MESSAGE));

        var fieldsResult = notifications.Count > 0 ? MethodResult.Error([.. notifications]) : MethodResult.Success();
        var result = MethodResult.Factory(fieldsResult, stateValueObject.MethodResult);

        if (result.IsError)
            return new VenueValueObject(isValid: false, methodResult: result);

        return new VenueValueObject(
            isValid: true,
            name: normalizedName,
            address: normalizedAddress,
            city: normalizedCity,
            state: stateValueObject,
            methodResult: MethodResult.Success());
    }

    private static string Normalize(string? value)
        => string.Join(' ', (value ?? string.Empty).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));

    private static InvalidOperationException InvalidValueObject()
        => new("It is not possible to get venue because value object is invalid");
}
