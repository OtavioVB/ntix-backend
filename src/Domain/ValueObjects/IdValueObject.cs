using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

public readonly struct IdValueObject
{
    public bool IsValid { get; }

    private readonly Guid _id;
    public Guid Value => IsValid
        ? _id
        : throw new InvalidOperationException("It is not possible to get Id because value object is invalid");

    public MethodResult MethodResult { get; }

    private IdValueObject(bool isValid, Guid id, MethodResult methodResult)
    {
        IsValid = isValid;
        _id = id;
        MethodResult = methodResult;
    }

    private const string INVALID_ID_NOTIFICATION_CODE = "INVALID_ID";
    private const string INVALID_ID_NOTIFICATION_MESSAGE = "O ID informado não possui um formato válido.";

    public static IdValueObject Build(string? id = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return new IdValueObject(
                isValid: true,
                id: Guid.NewGuid(),
                methodResult: MethodResult.Success());
        }

        if (Guid.TryParse(id, out var parsed))
        {
            return new IdValueObject(
                isValid: true,
                id: parsed,
                methodResult: MethodResult.Success());
        }

        var notification = Notification.Error(
            code: INVALID_ID_NOTIFICATION_CODE,
            message: INVALID_ID_NOTIFICATION_MESSAGE);

        return new IdValueObject(
            isValid: false,
            id: Guid.Empty,
            methodResult: MethodResult.Error([notification]));
    }
}
