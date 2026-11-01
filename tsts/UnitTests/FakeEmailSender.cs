using Infra.Resend.Base.EmailSender.Interfaces;
using System.Collections.Concurrent;

namespace TestSupport;

public sealed record SentEmail(string To, string Subject, string Html);

// Substitui a Resend nos testes integrados e funcionais, para não enviar emails reais.
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<string, bool> _failingRecipients = new();

    public ConcurrentQueue<SentEmail> Sent { get; } = new();

    public void FailFor(string to)
        => _failingRecipients[to] = true;

    public Task SendAsync(string to, string subject, string html, CancellationToken cancellationToken = default)
    {
        if (_failingRecipients.ContainsKey(to))
            throw new HttpRequestException("Falha simulada no envio.");

        Sent.Enqueue(new SentEmail(to, subject, html));

        return Task.CompletedTask;
    }
}
