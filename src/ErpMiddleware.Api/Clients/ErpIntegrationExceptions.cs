using System.Net;

namespace ErpMiddleware.Api.Clients;

public abstract class ErpIntegrationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class ErpTimeoutException(int timeoutSeconds, int attempts, Exception innerException)
    : ErpIntegrationException(
        $"ERP did not respond within {timeoutSeconds} {(timeoutSeconds == 1 ? "second" : "seconds")} after {attempts} attempts.",
        innerException)
{
    public int TimeoutSeconds { get; } = timeoutSeconds;
}

public sealed class ErpServerException(HttpStatusCode statusCode, int attempts)
    : ErpIntegrationException(
        $"ERP returned status code {(int)statusCode} after {attempts} attempts.")
{
    public int Attempts { get; } = attempts;
}

public sealed class ErpUnavailableException(int attempts, Exception innerException)
    : ErpIntegrationException(
        $"ERP could not be reached after {attempts} attempts.",
        innerException)
{
    public int Attempts { get; } = attempts;
}

public sealed class ErpRejectedException(HttpStatusCode statusCode)
    : ErpIntegrationException($"ERP rejected the request with status code {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class ErpInvalidResponseException(string message, Exception? innerException = null)
    : ErpIntegrationException(message, innerException);
