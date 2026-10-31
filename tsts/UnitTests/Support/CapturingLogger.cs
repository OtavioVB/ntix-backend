using Microsoft.Extensions.Logging;

namespace UnitTests.Support;

public sealed record CapturedLog(LogLevel Level, int EventId, string Message, Exception? Exception);

public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<CapturedLog> Logs { get; } = [];
    public List<object> Scopes { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        Scopes.Add(state);

        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
        => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Logs.Add(new CapturedLog(logLevel, eventId.Id, formatter(state, exception), exception));
}
