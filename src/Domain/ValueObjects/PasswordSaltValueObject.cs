using Domain.CrossCutting.MethodResultContext;
using System.Security.Cryptography;

namespace Domain.ValueObjects;

public readonly struct PasswordSaltValueObject
{
    public bool IsValid { get; }

    private readonly byte[]? _salt;
    public byte[] Value => IsValid
        ? _salt!
        : throw new InvalidOperationException("It is not possible to get password salt because value object is invalid");

    public MethodResult MethodResult { get; }

    private PasswordSaltValueObject(bool isValid, MethodResult methodResult, byte[]? salt = null)
    {
        IsValid = isValid;
        _salt = salt;
        MethodResult = methodResult;
    }

    public const int LENGTH = 16;

    public static PasswordSaltValueObject Build()
        => new(
            isValid: true,
            salt: RandomNumberGenerator.GetBytes(LENGTH),
            methodResult: MethodResult.Success());

    /// <summary>Salt gravado do usuário. Fora do tamanho, fica inválido.</summary>
    public static PasswordSaltValueObject Build(byte[]? salt)
        => salt is { Length: LENGTH }
            ? new PasswordSaltValueObject(isValid: true, salt: salt, methodResult: MethodResult.Success())
            : new PasswordSaltValueObject(isValid: false, methodResult: MethodResult.Error());

    /// <summary>
    /// Salt fixo do login com e-mail inexistente (REQ5, RN3): o hash é calculado do mesmo jeito, e o tempo da resposta
    /// não revela se a conta existe.
    /// </summary>
    public static PasswordSaltValueObject BuildForUnknownUser()
        => new(isValid: true, salt: new byte[LENGTH], methodResult: MethodResult.Success());
}
