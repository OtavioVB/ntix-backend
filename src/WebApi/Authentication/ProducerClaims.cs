namespace WebApi.Authentication;

/// <summary>Claims do access token do usuário do produtor. Só identificadores: nunca e-mail, nome ou documento.</summary>
public static class ProducerClaims
{
    public const string SUBJECT = "sub";
    public const string PRODUCER_ID = "producer_id";
    public const string PRODUCER_SLUG = "producer_slug";
    public const string USER_TYPE = "user_type";
}
