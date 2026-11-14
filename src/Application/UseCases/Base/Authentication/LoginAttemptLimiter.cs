using Application.UseCases.Base.Configuration;
using Domain.ValueObjects;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Application.UseCases.Base.Authentication;

/// <summary>
/// Limite de tentativas de login por e-mail (REQ5, RN12), em janela fixa que começa na primeira tentativa. Conta toda
/// tentativa com e-mail válido, existente ou não, aceita ou recusada.
/// <para>
/// Em memória e por instância da API: o reinício zera as janelas, o que é aceito enquanto a API roda em uma instância
/// na VPS. A chave é o SHA-256 do e-mail normalizado, para não guardar o dado pessoal em claro.
/// </para>
/// </summary>
public sealed class LoginAttemptLimiter
{
    private readonly ConcurrentDictionary<string, AttemptWindow> _windows = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly int _permitLimit;
    private readonly TimeSpan _window;

    private long _lastSweepTicks;

    public LoginAttemptLimiter(LoginAttemptConfiguration configuration, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuration.PermitLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuration.WindowMinutes);

        _permitLimit = configuration.PermitLimit;
        _window = configuration.Window;
        _timeProvider = timeProvider;
        _lastSweepTicks = timeProvider.GetUtcNow().UtcTicks;
    }

    /// <summary>Registra a tentativa. Falso quando o e-mail já usou todas as tentativas da janela.</summary>
    public bool TryAcquire(EmailValueObject email)
    {
        var now = _timeProvider.GetUtcNow();

        RemoveExpiredWindows(now);

        var window = _windows.GetOrAdd(GetKey(email), _ => new AttemptWindow(now));

        lock (window)
        {
            if (now - window.StartedAt >= _window)
            {
                window.StartedAt = now;
                window.Count = 0;
            }

            if (window.Count >= _permitLimit)
                return false;

            window.Count++;

            return true;
        }
    }

    /// <summary>Tempo até a janela do e-mail reabrir, em segundos inteiros (header <c>Retry-After</c>). Zero quando está aberta.</summary>
    public int GetRetryAfterSeconds(EmailValueObject email)
    {
        if (!email.IsValid || !_windows.TryGetValue(GetKey(email), out var window))
            return 0;

        var now = _timeProvider.GetUtcNow();

        lock (window)
        {
            var remaining = window.StartedAt + _window - now;

            if (window.Count < _permitLimit || remaining <= TimeSpan.Zero)
                return 0;

            return (int)Math.Ceiling(remaining.TotalSeconds);
        }
    }

    private static string GetKey(EmailValueObject email)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Value)));

    // Uma varredura por janela, para e-mails que não voltam não ocuparem memória para sempre.
    private void RemoveExpiredWindows(DateTimeOffset now)
    {
        var lastSweepTicks = Interlocked.Read(ref _lastSweepTicks);

        if (now.UtcTicks - lastSweepTicks < _window.Ticks
            || Interlocked.CompareExchange(ref _lastSweepTicks, now.UtcTicks, lastSweepTicks) != lastSweepTicks)
        {
            return;
        }

        foreach (var entry in _windows)
        {
            bool expired;

            lock (entry.Value)
                expired = now - entry.Value.StartedAt >= _window;

            if (expired)
                _windows.TryRemove(entry);
        }
    }

    private sealed class AttemptWindow(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; set; } = startedAt;
        public int Count { get; set; }
    }
}
