namespace Application.UseCases.Base.Authentication;

/// <summary>Claims do access token do usuário do produtor (REQ5, RN5). Só identificadores: nunca e-mail, nome ou documento.</summary>
public static class ProducerClaims
{
    public const string SUBJECT = "sub";
    public const string PRODUCER_ID = "producer_id";
    public const string PRODUCER_SLUG = "producer_slug";
    public const string USER_TYPE = "user_type";
    public const string SESSION_ID = "jti";
}
