using Domain.CrossCutting.MethodResultContext;
using System.Security.Cryptography;
using System.Text;

namespace Domain.ValueObjects;

public readonly struct EmailVerificationTokenValueObject
{
    public bool IsValid { get; }

    private readonly string? _token;
    public string Value => IsValid
        ? _token!
        : throw new InvalidOperationException("It is not possible to get token because value object is invalid");

    public MethodResult MethodResult { get; }

    private EmailVerificationTokenValueObject(bool isValid, MethodResult methodResult, string? token = null)
    {
        IsValid = isValid;
        _token = token;
        MethodResult = methodResult;
    }

    public const int HASH_LENGTH = SHA256.HashSizeInBytes;

    private const int RANDOM_BYTES_LENGTH = 32;

    public static EmailVerificationTokenValueObject Build()
        => new(
            isValid: true,
            token: Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(RANDOM_BYTES_LENGTH)),
            methodResult: MethodResult.Success());

    public byte[] GetHash()
        => SHA256.HashData(Encoding.UTF8.GetBytes(Value));
}
