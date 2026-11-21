using System.Security.Cryptography;

namespace Domain.ValueObjects;

/// <summary>
/// Código público do ingresso (REQ8, RN7): 12 caracteres do alfabeto sem os ambíguos, sorteados com <see cref="RandomNumberGenerator"/>.
/// É o conteúdo do QR e é único no sistema inteiro (<c>ux_ticket_code</c>), então é gerado pelo domínio, nunca pela entrada.
/// </summary>
public readonly struct TicketCodeValueObject
{
    public const int LENGTH = 12;
    public const string ALPHABET = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public string Value { get; }

    private TicketCodeValueObject(string value) => Value = value;

    public static TicketCodeValueObject Generate()
        => new(new string(RandomNumberGenerator.GetItems<char>(ALPHABET, LENGTH)));
}
