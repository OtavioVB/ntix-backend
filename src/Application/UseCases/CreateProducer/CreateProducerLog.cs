using Microsoft.Extensions.Logging;

namespace Application.UseCases.CreateProducer;

internal static partial class CreateProducerLog
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "[{MethodName}] Producer has been created. Input = {@Input}.")]
    public static partial void Created(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "[{MethodName}] Producer creation has been refused because email already exists. Input = {@Input}.")]
    public static partial void AlreadyExists(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Warning,
        Message = "[{MethodName}] Producer creation has been refused because document already exists. Input = {@Input}.")]
    public static partial void DocumentAlreadyExists(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning,
        Message = "[{MethodName}] Producer creation has been refused because input is invalid. Input = {@Input}.")]
    public static partial void InvalidData(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information,
        Message = "[{MethodName}] Producer slug has been taken by a concurrent creation, generating another version. Input = {@Input}.")]
    public static partial void SlugTaken(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Error,
        Message = "[{MethodName}] Producer creation has been failed. Input = {@Input}.")]
    public static partial void Failed(ILogger logger, string methodName, object input, Exception exception);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Error,
        Message = "[{MethodName}] Producer creation rollback has been failed. Input = {@Input}.")]
    public static partial void RollbackFailed(ILogger logger, string methodName, object input, Exception exception);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Information,
        Message = "[{MethodName}] Producer user verification email has been sent. Input = {@Input}.")]
    public static partial void VerificationEmailSent(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Error,
        Message = "[{MethodName}] Producer user verification email has been failed. Input = {@Input}.")]
    public static partial void VerificationEmailFailed(ILogger logger, string methodName, object input, Exception exception);
}
