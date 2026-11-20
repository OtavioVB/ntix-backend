using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Observação do lote (REQ10, RN2): opcional, até 140 caracteres (ex.: "Meia-entrada mediante documento").</summary>
public readonly struct BatchNoteValueObject
{
    public bool IsValid { get; }

    private readonly string? _note;

    /// <summary>Nula quando o produtor não informou observação.</summary>
    public string? Value => IsValid
        ? _note
        : throw new InvalidOperationException("It is not possible to get batch note because value object is invalid");

    public MethodResult MethodResult { get; }

    private BatchNoteValueObject(bool isValid, MethodResult methodResult, string? note = null)
    {
        IsValid = isValid;
        _note = note;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 140;

    private const string BATCH_NOTE_INVALID_NOTIFICATION_CODE = "BATCH_NOTE_INVALID";
    private const string BATCH_NOTE_INVALID_NOTIFICATION_MESSAGE = "A observação do lote deve ter até 140 caracteres.";

    public static BatchNoteValueObject Build(string? note)
    {
        var trimmed = note?.Trim();

        if (string.IsNullOrEmpty(trimmed))
            return new BatchNoteValueObject(isValid: true, methodResult: MethodResult.Success());

        if (trimmed.Length > MAX_LENGTH)
        {
            var notification = Notification.Error(BATCH_NOTE_INVALID_NOTIFICATION_CODE, BATCH_NOTE_INVALID_NOTIFICATION_MESSAGE);

            return new BatchNoteValueObject(isValid: false, methodResult: MethodResult.Error([notification]));
        }

        return new BatchNoteValueObject(isValid: true, note: trimmed, methodResult: MethodResult.Success());
    }

    public static implicit operator BatchNoteValueObject(string? obj)
        => Build(obj);
}
