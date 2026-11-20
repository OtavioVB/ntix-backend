using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Como o cliente se identifica no login (REQ12, RN1).</summary>
public enum CustomerLoginKind
{
    /// <summary>Não é CPF válido nem e-mail válido: o login segue até a senha e é recusado como credencial inválida.</summary>
    UNKNOWN = 0,
    DOCUMENT = 1,
    EMAIL = 2,
}

/// <summary>
/// Login do cliente (REQ12, RN1): 11 dígitos depois de retirar <c>.</c> e <c>-</c>, com CPF válido, é documento; senão,
/// e-mail normalizado como no cadastro. Formato inválido nos dois não vira notificação de formato: responde como credencial
/// inválida (401), sem dizer qual parte errou. Só vazio é recusado na entrada.
/// </summary>
public readonly struct CustomerLoginValueObject
{
    public bool IsValid { get; }

    public CustomerLoginKind Kind { get; }

    private readonly string? _value;

    /// <summary>CPF só com dígitos ou e-mail normalizado; para <see cref="CustomerLoginKind.UNKNOWN"/>, o texto aparado.</summary>
    public string Value => IsValid
        ? _value!
        : throw new InvalidOperationException("It is not possible to get customer login because value object is invalid");

    public MethodResult MethodResult { get; }

    private CustomerLoginValueObject(bool isValid, MethodResult methodResult, CustomerLoginKind kind = CustomerLoginKind.UNKNOWN, string? value = null, string? obfuscated = null)
    {
        IsValid = isValid;
        MethodResult = methodResult;
        Kind = kind;
        _value = value;
        _obfuscated = obfuscated;
    }

    private readonly string? _obfuscated;

    private const string LOGIN_REQUIRED_NOTIFICATION_CODE = "LOGIN_REQUIRED";
    private const string LOGIN_REQUIRED_NOTIFICATION_MESSAGE = "Informe o e-mail ou o CPF da sua conta.";

    public static CustomerLoginValueObject Build(string? login)
    {
        var trimmed = (login ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            return new(isValid: false, methodResult: MethodResult.Error([Notification.Error(LOGIN_REQUIRED_NOTIFICATION_CODE, LOGIN_REQUIRED_NOTIFICATION_MESSAGE)]));

        var digits = trimmed.Replace(".", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);

        if (digits.Length == CpfValueObject.LENGTH && digits.All(char.IsAsciiDigit))
        {
            var cpf = CpfValueObject.Build(digits);

            if (cpf.IsValid)
                return new(isValid: true, methodResult: MethodResult.Success(), kind: CustomerLoginKind.DOCUMENT, value: cpf.Value, obfuscated: cpf.GetObfuscatedValue());
        }

        var email = EmailValueObject.Build(trimmed);

        if (email.IsValid)
            return new(isValid: true, methodResult: MethodResult.Success(), kind: CustomerLoginKind.EMAIL, value: email.Value, obfuscated: email.GetObfuscatedValue());

        return new(isValid: true, methodResult: MethodResult.Success(), kind: CustomerLoginKind.UNKNOWN, value: trimmed, obfuscated: "***");
    }

    /// <summary>Chave do limite de tentativas: separada da do produtor e igual para o mesmo CPF com ou sem máscara (RN9).</summary>
    public string AttemptKey => $"customer:{Value.ToLowerInvariant()}";

    /// <summary>Login para log, no formato fixo do dado (<c>***.456.789-**</c> ou <c>ma***@ntix.com</c>).</summary>
    public string GetObfuscatedValue()
        => IsValid ? _obfuscated! : "-";

    public static implicit operator CustomerLoginValueObject(string? obj)
        => Build(obj);
}
