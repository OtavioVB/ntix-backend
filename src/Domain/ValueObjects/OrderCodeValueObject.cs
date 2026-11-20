using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using System.Security.Cryptography;

namespace Domain.ValueObjects;

/// <summary>
/// Código do pedido (REQ6, RN10): <c>NX</c> mais 8 caracteres de um alfabeto sem os ambíguos (0, 1, I, L, O),
/// sorteados com <see cref="RandomNumberGenerator"/>. É o que o comprador vê e o que a rota usa.
/// </summary>
public readonly struct OrderCodeValueObject
{
    public bool IsValid { get; }

    private readonly string? _code;
    public string Value => IsValid
        ? _code!
        : throw new InvalidOperationException("It is not possible to get order code because value object is invalid");

    public MethodResult MethodResult { get; }

    private OrderCodeValueObject(bool isValid, MethodResult methodResult, string? code = null)
    {
        IsValid = isValid;
        _code = code;
        MethodResult = methodResult;
    }

    public const string PREFIX = "NX";
    public const int RANDOM_PART_LENGTH = 8;
    public const string ALPHABET = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    private const string ORDER_CODE_INVALID_NOTIFICATION_CODE = "ORDER_CODE_INVALID";
    private const string ORDER_CODE_INVALID_NOTIFICATION_MESSAGE = "O código do pedido não possui um formato válido.";

    public static OrderCodeValueObject Generate()
        => new(isValid: true, methodResult: MethodResult.Success(), code: PREFIX + new string(RandomNumberGenerator.GetItems<char>(ALPHABET, RANDOM_PART_LENGTH)));

    /// <summary>Aceita o código em qualquer caixa e com espaços nas pontas; o valor guardado é o canônico (maiúsculo).</summary>
    public static OrderCodeValueObject Build(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant();

        if (normalized is null
            || normalized.Length != PREFIX.Length + RANDOM_PART_LENGTH
            || !normalized.StartsWith(PREFIX, StringComparison.Ordinal)
            || normalized[PREFIX.Length..].Any(character => !ALPHABET.Contains(character)))
        {
            var notification = Notification.Error(ORDER_CODE_INVALID_NOTIFICATION_CODE, ORDER_CODE_INVALID_NOTIFICATION_MESSAGE);

            return new OrderCodeValueObject(isValid: false, methodResult: MethodResult.Error([notification]));
        }

        return new OrderCodeValueObject(isValid: true, code: normalized, methodResult: MethodResult.Success());
    }
}
