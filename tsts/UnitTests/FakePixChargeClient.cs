using Infra.Http.Payments.Interfaces;
using Infra.Http.Payments.Models;

namespace TestSupport;

/// <summary>
/// Fornecedor de Pix falso para os testes: a esteira nunca chama o AbacatePay (REQ7). Por padrão gera um Pix novo a cada chamada;
/// uma <c>API_KEY</c> pode ter o resultado trocado para simular timeout, 500 ou recusa daquela conta.
/// </summary>
public sealed class FakePixChargeClient : IPixChargeClient
{
    private readonly object _lock = new();
    private readonly List<(string ApiKey, PixChargeRequestModel Request)> _calls = [];
    private readonly Dictionary<string, PixChargeResultModel> _resultsByApiKey = [];

    public string ProviderCode => "ABACATEPAY";

    public IReadOnlyList<(string ApiKey, PixChargeRequestModel Request)> Calls
    {
        get
        {
            lock (_lock)
                return _calls.ToArray();
        }
    }

    /// <summary>Faz as chamadas feitas com esta chave devolverem <paramref name="result"/>.</summary>
    public void FailWith(string apiKey, PixChargeResultModel result)
    {
        lock (_lock)
            _resultsByApiKey[apiKey] = result;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _calls.Clear();
            _resultsByApiKey.Clear();
        }
    }

    public Task<PixChargeResultModel> CreatePixChargeAsync(string apiKey, PixChargeRequestModel request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _calls.Add((apiKey, request));

            if (_resultsByApiKey.TryGetValue(apiKey, out var configured))
                return Task.FromResult(configured);
        }

        var suffix = Guid.NewGuid().ToString("N");

        return Task.FromResult(PixChargeResultModel.Created($"pix_char_{suffix}", $"00020101021226950014br.gov.bcb.pix{suffix}", "data:image/png;base64,iVBORw0KGgo="));
    }

    private readonly Dictionary<string, string> _statusByReference = [];

    /// <summary>Faz a consulta da cobrança devolver <paramref name="status"/> (ex.: PAID, PENDING). Sem configuração, responde PENDING.</summary>
    public void SetStatus(string providerReference, string status)
    {
        lock (_lock)
            _statusByReference[providerReference] = status;
    }

    /// <summary>Faz a consulta desta cobrança falhar, como a fonte fora do ar (REQ8: 503 no webhook).</summary>
    public void FailStatusQuery(string providerReference, string reason)
    {
        lock (_lock)
            _statusByReference[providerReference] = $"!{reason}";
    }

    public Task<PixChargeStatusResultModel> GetPixChargeStatusAsync(string apiKey, string providerReference, CancellationToken cancellationToken = default)
    {
        string? configured;

        lock (_lock)
            _statusByReference.TryGetValue(providerReference, out configured);

        configured ??= "PENDING";

        if (configured.StartsWith('!'))
            return Task.FromResult(PixChargeStatusResultModel.Unavailable(configured[1..]));

        return Task.FromResult(PixChargeStatusResultModel.Found(configured));
    }
}
