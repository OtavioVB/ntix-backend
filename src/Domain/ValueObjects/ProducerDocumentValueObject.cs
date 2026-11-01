using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

public readonly struct ProducerDocumentValueObject
{
    public bool IsValid { get; }

    private readonly string? _document;

    // Opcional: nulo quando a produtora não informou o documento.
    public string? Value => IsValid
        ? _document
        : throw new InvalidOperationException("It is not possible to get producer document because value object is invalid");

    public MethodResult MethodResult { get; }

    private ProducerDocumentValueObject(bool isValid, MethodResult methodResult, string? document = null)
    {
        IsValid = isValid;
        _document = document;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 14;

    private const int CNPJ_BASE_LENGTH = 12;
    private const int CHECK_DIGIT_MODULUS = 11;
    private const int CNPJ_MAX_WEIGHT = 9;
    private const int CNPJ_MIN_WEIGHT = 2;

    private const string PRODUCER_DOCUMENT_INVALID_NOTIFICATION_CODE = "PRODUCER_DOCUMENT_INVALID";
    private const string PRODUCER_DOCUMENT_INVALID_NOTIFICATION_MESSAGE = "O documento informado não é um CPF ou CNPJ válido.";

    public static ProducerDocumentValueObject Build(string? document)
    {
        var normalized = (document ?? string.Empty)
            .Trim()
            .Replace(".", string.Empty)
            .Replace("-", string.Empty)
            .Replace("/", string.Empty)
            .ToUpperInvariant();

        if (normalized.Length == 0)
        {
            return new ProducerDocumentValueObject(
                isValid: true,
                methodResult: MethodResult.Success());
        }

        if (!CpfValueObject.Build(normalized).IsValid && !IsValidCnpj(normalized))
        {
            var notification = Notification.Error(
                code: PRODUCER_DOCUMENT_INVALID_NOTIFICATION_CODE,
                message: PRODUCER_DOCUMENT_INVALID_NOTIFICATION_MESSAGE);

            return new ProducerDocumentValueObject(
                isValid: false,
                methodResult: MethodResult.Error([notification]));
        }

        return new ProducerDocumentValueObject(
            isValid: true,
            document: normalized,
            methodResult: MethodResult.Success());
    }

    private static bool IsValidCnpj(string cnpj)
    {
        if (cnpj.Length != MAX_LENGTH || cnpj.All(character => character == cnpj[0]))
            return false;

        if (!cnpj[..CNPJ_BASE_LENGTH].All(char.IsAsciiLetterOrDigit) || !cnpj[CNPJ_BASE_LENGTH..].All(char.IsAsciiDigit))
            return false;

        return CalculateCnpjCheckDigit(cnpj, CNPJ_BASE_LENGTH) == cnpj[CNPJ_BASE_LENGTH] - '0'
            && CalculateCnpjCheckDigit(cnpj, CNPJ_BASE_LENGTH + 1) == cnpj[CNPJ_BASE_LENGTH + 1] - '0';
    }

    // No CNPJ alfanumérico, o valor de cada caractere é o código ASCII menos 48.
    private static int CalculateCnpjCheckDigit(string cnpj, int length)
    {
        var sum = 0;
        var weight = CNPJ_MIN_WEIGHT;

        for (var i = length - 1; i >= 0; i--)
        {
            sum += (cnpj[i] - '0') * weight;
            weight = weight == CNPJ_MAX_WEIGHT ? CNPJ_MIN_WEIGHT : weight + 1;
        }

        var remainder = sum % CHECK_DIGIT_MODULUS;

        return remainder < 2 ? 0 : CHECK_DIGIT_MODULUS - remainder;
    }

    public static implicit operator ProducerDocumentValueObject(string? obj)
        => Build(obj);
}
