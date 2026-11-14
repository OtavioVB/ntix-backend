using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Situação da integração informada pelo produtor, aceita só pelo nome exato: ACTIVE ou INACTIVE.</summary>
public readonly struct IntegrationStatusValueObject
{
    public bool IsValid { get; }

    private readonly IntegrationStatus _status;
    public IntegrationStatus Value => IsValid
        ? _status
        : throw new InvalidOperationException("It is not possible to get integration status because value object is invalid");

    public MethodResult MethodResult { get; }

    private IntegrationStatusValueObject(bool isValid, MethodResult methodResult, IntegrationStatus status = default)
    {
        IsValid = isValid;
        _status = status;
        MethodResult = methodResult;
    }

    private const string INTEGRATION_STATUS_INVALID_NOTIFICATION_CODE = "INTEGRATION_STATUS_INVALID";
    private const string INTEGRATION_STATUS_INVALID_NOTIFICATION_MESSAGE = "A situação da integração deve ser ACTIVE ou INACTIVE.";

    public static IntegrationStatusValueObject Build(string? status)
    {
        if (status is null || !Enum.GetNames<IntegrationStatus>().Contains(status))
        {
            var notification = Notification.Error(
                code: INTEGRATION_STATUS_INVALID_NOTIFICATION_CODE,
                message: INTEGRATION_STATUS_INVALID_NOTIFICATION_MESSAGE);

            return new IntegrationStatusValueObject(
                isValid: false,
                methodResult: MethodResult.Error([notification]));
        }

        return new IntegrationStatusValueObject(
            isValid: true,
            status: Enum.Parse<IntegrationStatus>(status),
            methodResult: MethodResult.Success());
    }

    public static implicit operator IntegrationStatusValueObject(string? obj)
        => Build(obj);
}
