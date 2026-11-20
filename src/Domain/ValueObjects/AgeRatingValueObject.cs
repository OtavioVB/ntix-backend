using Domain.BoundedContexts.EventContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Classificação etária do evento (RN8), aceita só pelo nome exato.</summary>
public readonly struct AgeRatingValueObject
{
    public bool IsValid { get; }

    private readonly AgeRating _value;
    public AgeRating Value => IsValid
        ? _value
        : throw new InvalidOperationException("It is not possible to get AgeRating because value object is invalid");

    public MethodResult MethodResult { get; }

    private AgeRatingValueObject(bool isValid, MethodResult methodResult, AgeRating value = default)
    {
        IsValid = isValid;
        _value = value;
        MethodResult = methodResult;
    }

    private const string EVENT_AGE_RATING_INVALID_NOTIFICATION_CODE = "EVENT_AGE_RATING_INVALID";
    private const string EVENT_AGE_RATING_INVALID_NOTIFICATION_MESSAGE = "A classificação etária deve ser uma de FREE, AGE_10, AGE_12, AGE_14, AGE_16 ou AGE_18.";

    public static AgeRatingValueObject Build(string? value)
    {
        if (value is null || !Enum.GetNames<AgeRating>().Contains(value))
        {
            var notification = Notification.Error(
                code: EVENT_AGE_RATING_INVALID_NOTIFICATION_CODE,
                message: EVENT_AGE_RATING_INVALID_NOTIFICATION_MESSAGE);

            return new AgeRatingValueObject(
                isValid: false,
                methodResult: MethodResult.Error([notification]));
        }

        return new AgeRatingValueObject(
            isValid: true,
            value: Enum.Parse<AgeRating>(value),
            methodResult: MethodResult.Success());
    }

    public static implicit operator AgeRatingValueObject(string? obj)
        => Build(obj);
}
