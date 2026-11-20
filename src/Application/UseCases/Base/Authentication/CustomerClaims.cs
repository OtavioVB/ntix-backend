namespace Application.UseCases.Base.Authentication;

/// <summary>
/// Claims do access token do cliente (REQ12, RN6). Só identificadores: nunca nome, e-mail ou CPF. O claim <see cref="ACTOR"/>
/// separa o token do cliente do token do produtor (RN7).
/// </summary>
public static class CustomerClaims
{
    public const string SUBJECT = "sub";
    public const string ACTOR = "actor";
    public const string SESSION_ID = "jti";

    public const string CUSTOMER_ACTOR = "CUSTOMER";
}
