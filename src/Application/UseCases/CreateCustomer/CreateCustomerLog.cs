using Microsoft.Extensions.Logging;

namespace Application.UseCases.CreateCustomer;

internal static partial class CreateCustomerLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "[{MethodName}] Customer has been created. Input = {@Input}.")]
    public static partial void Created(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "[{MethodName}] Customer creation has been refused because email or document already exists. Input = {@Input}.")]
    public static partial void AlreadyExists(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "[{MethodName}] Customer creation has been refused because input is invalid. Input = {@Input}.")]
    public static partial void InvalidData(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "[{MethodName}] Customer slug has been taken by a concurrent creation, generating another version. Input = {@Input}.")]
    public static partial void SlugTaken(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error,
        Message = "[{MethodName}] Customer creation has been failed. Input = {@Input}.")]
    public static partial void Failed(ILogger logger, string methodName, object input, Exception exception);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Error,
        Message = "[{MethodName}] Customer creation rollback has been failed. Input = {@Input}.")]
    public static partial void RollbackFailed(ILogger logger, string methodName, object input, Exception exception);
}
