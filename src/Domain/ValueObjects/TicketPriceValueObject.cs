using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>
/// Preço do ingresso em centavos (REQ10, RN3): de 100 (R$ 1,00, o mínimo do Pix no AbacatePay) a 10.000.000 (R$ 100.000,00).
/// Dinheiro trafega em centavos inteiros; o banco guarda em reais com duas casas.
/// </summary>
public readonly struct TicketPriceValueObject
{
    public bool IsValid { get; }

    private readonly long _cents;
    public long Cents => IsValid
        ? _cents
        : throw new InvalidOperationException("It is not possible to get ticket price because value object is invalid");

    /// <summary>Em reais, com duas casas, para o <c>NUMERIC(18,2)</c> do banco. A conversão é exata.</summary>
    public decimal Amount => Cents / 100m;

    public MethodResult MethodResult { get; }

    private TicketPriceValueObject(bool isValid, MethodResult methodResult, long cents = 0)
    {
        IsValid = isValid;
        _cents = cents;
        MethodResult = methodResult;
    }

    public const long MIN_CENTS = 100;
    public const long MAX_CENTS = 10_000_000;

    private const string BATCH_PRICE_INVALID_NOTIFICATION_CODE = "BATCH_PRICE_INVALID";
    private const string BATCH_PRICE_INVALID_NOTIFICATION_MESSAGE = "O preço do lote deve ser de R$ 1,00 a R$ 100.000,00, informado em centavos.";

    public static TicketPriceValueObject Build(long? cents)
    {
        if (cents is null or < MIN_CENTS or > MAX_CENTS)
        {
            var notification = Notification.Error(BATCH_PRICE_INVALID_NOTIFICATION_CODE, BATCH_PRICE_INVALID_NOTIFICATION_MESSAGE);

            return new TicketPriceValueObject(isValid: false, methodResult: MethodResult.Error([notification]));
        }

        return new TicketPriceValueObject(isValid: true, cents: cents.Value, methodResult: MethodResult.Success());
    }

    /// <summary>Preço gravado em reais (<c>NUMERIC(18,2)</c>), convertido de volta para centavos.</summary>
    public static TicketPriceValueObject FromAmount(decimal amount)
        => Build((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
}
