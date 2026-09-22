using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpMiddleware.Api.Clients;
using ErpMiddleware.Api.Contracts;

namespace ErpMiddleware.Tests.Infrastructure;

public sealed class FakeErpHttpMessageHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, int> _callCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FakeErpCall> _calls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FakeErpBehavior> _behaviors = new(StringComparer.Ordinal);

    public void SetBehavior(string idempotencyKey, FakeErpBehavior behavior) =>
        _behaviors[idempotencyKey] = behavior;

    public int GetCallCount(string idempotencyKey) =>
        _callCounts.GetValueOrDefault(idempotencyKey);

    public FakeErpCall? GetLastCall(string idempotencyKey) =>
        _calls.GetValueOrDefault(idempotencyKey);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
        var correlationId = request.Headers.GetValues("X-Correlation-ID").Single();
        var behavior = _behaviors.GetValueOrDefault(idempotencyKey);

        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var outboundOrder = JsonSerializer.Deserialize<OutboundOrderRequest>(body, JsonOptions)!;

        _callCounts[idempotencyKey] = GetCallCount(idempotencyKey) + 1;
        _calls[idempotencyKey] = new FakeErpCall(outboundOrder, correlationId);

        switch (behavior)
        {
            case FakeErpBehavior.Timeout:
                await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
                break;

            case FakeErpBehavior.ServerError:
                return CreateJsonResponse(
                    HttpStatusCode.InternalServerError,
                    new { status = "error", errorCode = "SIMULATED_ERP_ERROR" });

            case FakeErpBehavior.SlowResponse:
                await Task.Delay(50, cancellationToken);
                break;

            case FakeErpBehavior.PartialSuccess:
                return CreateJsonResponse(
                    (HttpStatusCode)207,
                    new ErpOrderAcknowledgement(
                        "partial_success",
                        outboundOrder.OrderCode,
                        "ERP processed some order items and rejected others",
                        [outboundOrder.Items[0].Code],
                        [new RejectedErpItem(
                            outboundOrder.Items[1].Code,
                            "Simulated item rejection")]));
        }

        return CreateJsonResponse(
            HttpStatusCode.OK,
            new ErpOrderAcknowledgement(
                "success",
                outboundOrder.OrderCode,
                "Order accepted by ERP"));
    }

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, object body) =>
        new(statusCode)
        {
            Content = JsonContent.Create(body)
        };
}

public sealed record FakeErpCall(
    OutboundOrderRequest Request,
    string CorrelationId);

public enum FakeErpBehavior
{
    Success,
    Timeout,
    ServerError,
    SlowResponse,
    PartialSuccess
}
