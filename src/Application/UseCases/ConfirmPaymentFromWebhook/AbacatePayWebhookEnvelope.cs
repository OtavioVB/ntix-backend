using System.Text.Json;

namespace Application.UseCases.ConfirmPaymentFromWebhook;

/// <summary>
/// Campos usados do envelope do AbacatePay (REQ8). Leitura tolerante: o id do envelope, o tipo do evento e, nos eventos de cobrança,
/// o id da cobrança. Os demais campos são ignorados.
/// </summary>
internal sealed record AbacatePayWebhookEnvelope(string Id, string EventType, string TransparentId)
{
    public const string COMPLETED_EVENT_TYPE = "transparent.completed";
    public const int MAX_ID_LENGTH = 128;

    public bool IsCompleted => EventType == COMPLETED_EVENT_TYPE;

    public static bool TryParse(ReadOnlySpan<byte> rawBody, out AbacatePayWebhookEnvelope? envelope)
    {
        envelope = null;

        try
        {
            using var document = JsonDocument.Parse(rawBody.ToArray());
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !TryGetString(root, "id", out var id) || !TryGetString(root, "event", out var eventType))
                return false;

            if (id.Length is 0 or > MAX_ID_LENGTH)
                return false;

            var transparentId = string.Empty;

            if (eventType == COMPLETED_EVENT_TYPE
                && (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("transparent", out var transparent) || !TryGetString(transparent, "id", out transparentId)))
                return false;

            envelope = new AbacatePayWebhookEnvelope(id, eventType, transparentId);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString() ?? string.Empty;

        return true;
    }
}
