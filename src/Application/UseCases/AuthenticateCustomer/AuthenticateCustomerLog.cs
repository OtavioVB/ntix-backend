using Microsoft.Extensions.Logging;

namespace Application.UseCases.AuthenticateCustomer;

internal static partial class AuthenticateCustomerLog
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "[{MethodName}] Customer has been authenticated. Input = {@Input}.")]
    public static partial void Authenticated(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "[{MethodName}] Customer authentication has been refused because input is invalid. Input = {@Input}.")]
    public static partial void InvalidData(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning,
        Message = "[{MethodName}] Customer authentication has been refused because customer was not found. Input = {@Input}.")]
    public static partial void CustomerNotFound(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning,
        Message = "[{MethodName}] Customer authentication has been refused because password is incorrect. Input = {@Input}.")]
    public static partial void InvalidPassword(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Warning,
        Message = "[{MethodName}] Customer authentication has been refused because customer is not linked to producer. Input = {@Input}.")]
    public static partial void NotLinkedToProducer(ILogger logger, string methodName, object input);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Error,
        Message = "[{MethodName}] Customer authentication has been failed. Input = {@Input}.")]
    public static partial void Failed(ILogger logger, string methodName, object input, Exception exception);
}
