using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using ErpMiddleware.Api.Configuration;
using ErpMiddleware.Api.Contracts;
using ErpMiddleware.Api.Middleware;
using ErpMiddleware.Api.Simulation;
using Microsoft.Extensions.Options;

namespace ErpMiddleware.Api.Clients;

public sealed class ErpOrderClient(
    HttpClient httpClient,
    IOptions<ErpOptions> options,
    ILogger<ErpOrderClient> logger) : IErpOrderClient
{
    private readonly ErpOptions _options = options.Value;

    public async Task<ErpOrderAcknowledgement> SendAsync(
        OutboundOrderRequest request,
        string idempotencyKey,
        string correlationId,
        string? simulationMode,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

                using var message = CreateRequestMessage(
                    request,
                    idempotencyKey,
                    correlationId,
                    simulationMode);

                logger.LogInformation(
                    "Sending order {OrderCode} to ERP. Attempt {AttemptNumber}/{MaxAttempts}, idempotency key {IdempotencyKey}",
                    request.OrderCode,
                    attempt,
                    _options.MaxAttempts,
                    idempotencyKey);

                using var response = await httpClient.SendAsync(message, timeoutSource.Token);

                logger.LogInformation(
                    "ERP responded to order {OrderCode} with status {StatusCode} in {ElapsedMilliseconds} ms on attempt {AttemptNumber}",
                    request.OrderCode,
                    (int)response.StatusCode,
                    stopwatch.ElapsedMilliseconds,
                    attempt);

                if ((int)response.StatusCode >= 500)
                {
                    if (attempt == _options.MaxAttempts)
                    {
                        throw new ErpServerException(response.StatusCode, attempt);
                    }

                    await DelayBeforeRetryAsync(attempt, "ERP server error", cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new ErpRejectedException(response.StatusCode);
                }

                try
                {
                    var acknowledgement = await response.Content.ReadFromJsonAsync<ErpOrderAcknowledgement>(
                        cancellationToken: timeoutSource.Token);

                    return acknowledgement
                        ?? throw new ErpInvalidResponseException("ERP returned an empty response body.");
                }
                catch (JsonException exception)
                {
                    throw new ErpInvalidResponseException(
                        "ERP returned a response that could not be parsed.",
                        exception);
                }
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "ERP timeout for order {OrderCode} after {TimeoutSeconds} seconds on attempt {AttemptNumber}/{MaxAttempts}",
                    request.OrderCode,
                    _options.TimeoutSeconds,
                    attempt,
                    _options.MaxAttempts);

                if (attempt == _options.MaxAttempts)
                {
                    throw new ErpTimeoutException(
                        _options.TimeoutSeconds,
                        attempt,
                        exception);
                }

                await DelayBeforeRetryAsync(attempt, "ERP timeout", cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(
                    exception,
                    "Network error while sending order {OrderCode} on attempt {AttemptNumber}/{MaxAttempts}",
                    request.OrderCode,
                    attempt,
                    _options.MaxAttempts);

                if (attempt == _options.MaxAttempts)
                {
                    throw new ErpUnavailableException(attempt, exception);
                }

                await DelayBeforeRetryAsync(attempt, "ERP network error", cancellationToken);
            }
        }

        throw new UnreachableException("The ERP retry loop completed without a result.");
    }

    private static HttpRequestMessage CreateRequestMessage(
        OutboundOrderRequest request,
        string idempotencyKey,
        string correlationId,
        string? simulationMode)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/erp/outbound/order")
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        message.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, correlationId);

        if (simulationMode is not null)
        {
            message.Headers.TryAddWithoutValidation(ErpSimulationModes.HeaderName, simulationMode);
        }

        return message;
    }

    private async Task DelayBeforeRetryAsync(
        int failedAttempt,
        string reason,
        CancellationToken cancellationToken)
    {
        var delayMilliseconds = _options.InitialBackoffMilliseconds * Math.Pow(2, failedAttempt - 1);
        var delay = TimeSpan.FromMilliseconds(delayMilliseconds);

        logger.LogWarning(
            "Retrying ERP request after {DelayMilliseconds} ms because of {RetryReason}",
            delay.TotalMilliseconds,
            reason);

        await Task.Delay(delay, cancellationToken);
    }
}
