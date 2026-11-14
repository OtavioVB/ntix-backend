using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace Domain.ValueObjects;

/// <summary>
/// Refresh token da sessão do usuário do produtor (REQ5, RN6): 32 bytes aleatórios, entregues em Base64Url (43
/// caracteres) e gravados só como SHA-256. O valor em claro existe apenas na resposta do login ou da renovação.
/// </summary>
public readonly struct RefreshTokenValueObject
{
    public bool IsValid { get; }

    private readonly string? _token;
    public string Value => IsValid
        ? _token!
        : throw new InvalidOperationException("It is not possible to get refresh token because value object is invalid");

    private readonly byte[]? _hash;

    /// <summary>SHA-256 do token, que é o que vai para o banco (<c>refresh_token_hash</c>).</summary>
    public byte[] Hash => IsValid
        ? _hash!
        : throw new InvalidOperationException("It is not possible to get refresh token hash because value object is invalid");

    public MethodResult MethodResult { get; }

    private RefreshTokenValueObject(bool isValid, MethodResult methodResult, string? token = null, byte[]? hash = null)
    {
        IsValid = isValid;
        _token = token;
        _hash = hash;
        MethodResult = methodResult;
    }

    public const int LENGTH = 32;
    public const int HASH_LENGTH = 32;

    private const string REFRESH_TOKEN_INVALID_NOTIFICATION_CODE = "REFRESH_TOKEN_INVALID";
    private const string REFRESH_TOKEN_INVALID_NOTIFICATION_MESSAGE = "O refresh token informado não possui um formato válido.";

    public static RefreshTokenValueObject Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(LENGTH);

        return new RefreshTokenValueObject(
            isValid: true,
            token: Base64Url.EncodeToString(bytes),
            hash: SHA256.HashData(bytes),
            methodResult: MethodResult.Success());
    }

    /// <summary>
    /// Token recebido de quem chama. Só aceita a forma canônica de 32 bytes, para dois textos diferentes nunca
    /// apontarem para a mesma sessão.
    /// </summary>
    public static RefreshTokenValueObject Build(string? token)
    {
        var bytes = new byte[LENGTH];

        if (token is not null
            && Base64Url.IsValid(token.AsSpan(), out var decodedLength)
            && decodedLength == LENGTH
            && Base64Url.TryDecodeFromChars(token.AsSpan(), bytes, out var written)
            && written == LENGTH
            && string.Equals(Base64Url.EncodeToString(bytes), token, StringComparison.Ordinal))
        {
            return new RefreshTokenValueObject(
                isValid: true,
                token: token,
                hash: SHA256.HashData(bytes),
                methodResult: MethodResult.Success());
        }

        return new RefreshTokenValueObject(
            isValid: false,
            methodResult: MethodResult.Error([Notification.Error(
                code: REFRESH_TOKEN_INVALID_NOTIFICATION_CODE,
                message: REFRESH_TOKEN_INVALID_NOTIFICATION_MESSAGE)]));
    }

    public static implicit operator RefreshTokenValueObject(string? obj)
        => Build(obj);
}
