using Domain.CrossCutting.MethodResultContext;
using System.Security.Cryptography;
using System.Text;

namespace Domain.ValueObjects;

public readonly struct PasswordHashValueObject
{
    public bool IsValid { get; }

    private readonly byte[]? _hash;
    public byte[] Value => IsValid
        ? _hash!
        : throw new InvalidOperationException("It is not possible to get password hash because value object is invalid");

    public MethodResult MethodResult { get; }

    private PasswordHashValueObject(bool isValid, MethodResult methodResult, byte[]? hash = null)
    {
        IsValid = isValid;
        _hash = hash;
        MethodResult = methodResult;
    }

    public const int LENGTH = 32;
    public const int ITERATIONS = 600_000;

    // O hash da senha com o salt é combinado à chave privada do servidor: sem ela, um vazamento da base não permite testar senhas.
    public static PasswordHashValueObject Build(PasswordValueObject password, PasswordSaltValueObject salt, string privateKey)
    {
        var passwordHash = Rfc2898DeriveBytes.Pbkdf2(
            password: password.Value,
            salt: salt.Value,
            iterations: ITERATIONS,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: LENGTH);

        return new PasswordHashValueObject(
            isValid: true,
            hash: HMACSHA256.HashData(Encoding.UTF8.GetBytes(privateKey), passwordHash),
            methodResult: MethodResult.Success());
    }
}
