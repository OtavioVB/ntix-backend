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

    public static PasswordSaltValueObject Build(byte[] salt)
        => new(
            isValid: true,
            salt: salt,
            methodResult: MethodResult.Success());
}
