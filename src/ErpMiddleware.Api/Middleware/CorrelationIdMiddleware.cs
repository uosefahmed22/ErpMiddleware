namespace ErpMiddleware.Api.Middleware;

public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var suppliedId)
            && !string.IsNullOrWhiteSpace(suppliedId)
                ? suppliedId.ToString()
                : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            logger.LogInformation(
                "Handling {HttpMethod} {RequestPath} with correlation ID {CorrelationId}",
                context.Request.Method,
                context.Request.Path,
                correlationId);

            await next(context);

            logger.LogInformation(
                "Completed {HttpMethod} {RequestPath} with status {StatusCode} and correlation ID {CorrelationId}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                correlationId);
        }
    }
}
