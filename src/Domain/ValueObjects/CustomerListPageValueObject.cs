using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>
/// Paginação das listas do comprador (REQ13, RN2): <c>page</c> a partir de 1 e <c>pageSize</c> de 1 a 50 (padrão 20), como na vitrine (REQ11).
/// Valor fora do intervalo recusa a consulta, com o código do filtro da lista.
/// </summary>
public readonly struct CustomerListPageValueObject
{
    public const int DEFAULT_PAGE_SIZE = 20;
    public const int MAX_PAGE_SIZE = 50;

    public bool IsValid { get; }

    public int Page { get; }

    public int PageSize { get; }

    public int Offset => (Page - 1) * PageSize;

    public MethodResult MethodResult { get; }

    private CustomerListPageValueObject(bool isValid, int page, int pageSize, MethodResult methodResult)
    {
        IsValid = isValid;
        Page = page;
        PageSize = pageSize;
        MethodResult = methodResult;
    }

    public static CustomerListPageValueObject Build(int? page, int? pageSize, string invalidFilterCode)
    {
        var resolvedPage = page ?? 1;
        var resolvedPageSize = pageSize ?? DEFAULT_PAGE_SIZE;

        if (resolvedPage < 1 || resolvedPageSize < 1 || resolvedPageSize > MAX_PAGE_SIZE)
        {
            var notification = Notification.Error(invalidFilterCode, $"A página deve ser a partir de 1 e o tamanho da página de 1 a {MAX_PAGE_SIZE}.");

            return new CustomerListPageValueObject(isValid: false, page: 1, pageSize: DEFAULT_PAGE_SIZE, methodResult: MethodResult.Error([notification]));
        }

        return new CustomerListPageValueObject(isValid: true, page: resolvedPage, pageSize: resolvedPageSize, methodResult: MethodResult.Success());
    }
}
