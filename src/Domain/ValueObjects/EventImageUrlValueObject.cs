using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>
/// Imagem do evento (RN9): opcional, só <c>https</c> absoluto, até 2048 caracteres. Reaproveita a validação do
/// <see cref="AssetUrlValueObject"/> e recusa o caminho relativo, porque a imagem aparece no hub e em lojas de outros domínios.
/// </summary>
public readonly struct EventImageUrlValueObject
{
    public bool IsValid { get; }

    private readonly string? _url;

    /// <summary>Nula quando o produtor não informou imagem.</summary>
    public string? Value => IsValid
        ? _url
        : throw new InvalidOperationException("It is not possible to get event image url because value object is invalid");

    public MethodResult MethodResult { get; }

    private EventImageUrlValueObject(bool isValid, MethodResult methodResult, string? url = null)
    {
        IsValid = isValid;
        _url = url;
        MethodResult = methodResult;
    }

    private const string EVENT_IMAGE_URL_INVALID_NOTIFICATION_CODE = "EVENT_IMAGE_URL_INVALID";
    private const string EVENT_IMAGE_URL_INVALID_NOTIFICATION_MESSAGE = "A imagem do evento deve ser um endereço https:// com até 2048 caracteres.";

    public static EventImageUrlValueObject Build(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new EventImageUrlValueObject(isValid: true, methodResult: MethodResult.Success());

        var asset = AssetUrlValueObject.Build(url);

        if (!asset.IsValid || !asset.Value.StartsWith("https://", StringComparison.Ordinal))
        {
            var notification = Notification.Error(
                code: EVENT_IMAGE_URL_INVALID_NOTIFICATION_CODE,
                message: EVENT_IMAGE_URL_INVALID_NOTIFICATION_MESSAGE);

            return new EventImageUrlValueObject(isValid: false, methodResult: MethodResult.Error([notification]));
        }

        return new EventImageUrlValueObject(isValid: true, url: asset.Value, methodResult: MethodResult.Success());
    }

    public static implicit operator EventImageUrlValueObject(string? obj)
        => Build(obj);
}
