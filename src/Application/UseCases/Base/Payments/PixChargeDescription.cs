using System.Globalization;

namespace Application.UseCases.Base.Payments;

/// <summary>Descrição da cobrança no fornecedor (REQ7, RN8): "Pedido {código} - {título}", até 140 caracteres, só caracteres imprimíveis e sem emoji.</summary>
public static class PixChargeDescription
{
    public const int MAX_LENGTH = 140;

    public static string Build(string orderCode, string eventTitle)
    {
        var printable = string.Concat($"Pedido {orderCode} - {eventTitle}".Where(IsPrintable)).Trim();

        return printable.Length <= MAX_LENGTH ? printable : printable[..MAX_LENGTH].TrimEnd();
    }

    // Emoji e símbolos ficam em pares substitutos ou na categoria de símbolos; letras, números, pontuação e espaço passam.
    private static bool IsPrintable(char character)
        => !char.IsControl(character)
            && !char.IsSurrogate(character)
            && CharUnicodeInfo.GetUnicodeCategory(character) is not (UnicodeCategory.OtherSymbol or UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned);
}
