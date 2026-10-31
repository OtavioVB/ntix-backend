using Domain.CrossCutting.MethodResultContext;

namespace Domain.ValueObjects;

public readonly struct DateTimeValueObject
{
    public bool IsValid { get; }

    private readonly DateTime _dateTime;
    public DateTime Value => IsValid
        ? _dateTime
        : throw new InvalidOperationException("It is not possible to get DateTime because value object is invalid.");

    public MethodResult MethodResult { get; }

    private DateTimeValueObject(bool isValid, DateTime value, MethodResult methodResult)
    {
        IsValid = isValid;
        _dateTime = value;
        MethodResult = methodResult;
    }

    public static DateTimeValueObject Build(DateTime dateTime)
    {
        return new DateTimeValueObject(
            isValid: true,
            value: dateTime,
            methodResult: MethodResult.Success());
    }
}
