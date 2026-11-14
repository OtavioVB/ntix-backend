using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>
/// Valor de uma credencial de integração de pagamento (ex.: a API key do fornecedor). Nunca vai para log, trace
/// nem resposta: o que sai é <see cref="GetObfuscatedValue"/>.
/// </summary>
public readonly struct CredentialValueObject
{
    public bool IsValid { get; }

    private readonly string? _value;
    public string Value => IsValid
        ? _value!
        : throw new InvalidOperationException("It is not possible to get credential because value object is invalid");

    public MethodResult MethodResult { get; }

    private CredentialValueObject(bool isValid, MethodResult methodResult, string? value = null)
    {
        IsValid = isValid;
        _value = value;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 2048;

    /// <summary>Quantidade máxima de caracteres finais exibidos no <c>valueHint</c>.</summary>
    public const int HINT_LENGTH = 4;

    private const string CREDENTIAL_VALUE_INVALID_NOTIFICATION_CODE = "CREDENTIAL_VALUE_INVALID";
    private const string CREDENTIAL_VALUE_INVALID_NOTIFICATION_MESSAGE = "O valor da credencial não pode ser vazio, ter espaço no início ou no fim, nem passar de 2048 caracteres.";

    /// <summary>Não normaliza: um espaço nas pontas é recusado, em vez de removido, para não gravar uma chave diferente da colada (RN5).</summary>
    public static CredentialValueObject Build(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MAX_LENGTH || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
        {
            var notification = Notification.Error(
                code: CREDENTIAL_VALUE_INVALID_NOTIFICATION_CODE,
                message: CREDENTIAL_VALUE_INVALID_NOTIFICATION_MESSAGE);

            return new CredentialValueObject(
                isValid: false,
                methodResult: MethodResult.Error([notification]));
        }

        return new CredentialValueObject(
            isValid: true,
            value: value,
            methodResult: MethodResult.Success());
    }

    /// <summary>
    /// Últimos caracteres do valor, para o produtor reconhecer a credencial sem vê-la (RN10). Nunca passa de metade
    /// do valor, para uma credencial curta não ser revelada inteira.
    /// </summary>
    public string GetObfuscatedValue()
    {
        var value = Value;
        var length = Math.Min(HINT_LENGTH, value.Length / 2);

        return value[^length..];
    }

    public static implicit operator CredentialValueObject(string? obj)
        => Build(obj);
}
