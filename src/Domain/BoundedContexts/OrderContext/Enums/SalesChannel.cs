namespace Domain.BoundedContexts.OrderContext.Enums;

/// <summary>Canal da venda (REQ6, RN2): hub da Ntix ou loja do produtor. Afiliado fica fora do escopo.</summary>
public enum SalesChannel
{
    HUB = 1,
    STORE = 2,
}
