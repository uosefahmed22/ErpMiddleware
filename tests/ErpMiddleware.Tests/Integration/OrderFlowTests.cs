using System.Net;
using System.Net.Http.Json;
using ErpMiddleware.Api.Contracts;
using ErpMiddleware.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ErpMiddleware.Tests.Integration;

public sealed class OrderFlowTests : IClassFixture<ErpMiddlewareApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FakeErpHttpMessageHandler _erpHandler;

    public OrderFlowTests(ErpMiddlewareApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _erpHandler = factory.ErpHandler;
    }

    [Fact]
    public async Task ValidRequest_ReturnsSuccessAndSendsTransformedPayload()
    {
        var key = NewKey("valid");
        const string correlationId = "integration-valid-correlation";

        using var response = await SendOrderAsync(key, "SO-TEST-VALID", correlationId: correlationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseJson = await response.Content.ReadFromJsonAsync<ProcessingSuccessResponse>();
        Assert.NotNull(responseJson);
        Assert.Equal("success", responseJson.Status);
        Assert.Equal("SO-TEST-VALID", responseJson.ErpOrderId);
        Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal(1, _erpHandler.GetCallCount(key));

        var outboundCall = Assert.IsType<FakeErpCall>(_erpHandler.GetLastCall(key));
        Assert.Equal(correlationId, outboundCall.CorrelationId);

        Assert.Equal("C1001", outboundCall.Request.CustomerId);
        Assert.Equal("John Doe", outboundCall.Request.FullName);
        Assert.Equal("SO-TEST-VALID", outboundCall.Request.OrderCode);
        Assert.Equal(200m, outboundCall.Request.TotalAmount);
        Assert.Equal(2, outboundCall.Request.Items.Count);
    }

    [Fact]
    public async Task MissingFields_ReturnsBadRequestWithoutCallingErp()
    {
        var key = NewKey("missing");

        using var response = await _client.PostAsJsonAsync(
            "/erp/inbound/order",
            new { idempotencyKey = key });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _erpHandler.GetCallCount(key));

        var responseJson = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(responseJson);
        Assert.Contains("Customer", responseJson.Errors.Keys);
        Assert.Contains("Order", responseJson.Errors.Keys);
    }

    [Fact]
    public async Task DuplicateRequest_ReturnsStoredResponseWithoutSecondErpCall()
    {
        var key = NewKey("duplicate");

        using var firstResponse = await SendOrderAsync(key, "SO-TEST-DUPLICATE");
        using var replayResponse = await SendOrderAsync(key, "SO-TEST-DUPLICATE");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal("true", replayResponse.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(1, _erpHandler.GetCallCount(key));

        Assert.Equal(
            await firstResponse.Content.ReadAsStringAsync(),
            await replayResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ErpTimeout_RetriesThreeTimesAndReturnsGatewayTimeout()
    {
        var key = NewKey("timeout");
        _erpHandler.SetBehavior(key, FakeErpBehavior.Timeout);

        using var response = await SendOrderAsync(key, "SO-TEST-TIMEOUT");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal(3, _erpHandler.GetCallCount(key));

        var responseJson = await response.Content.ReadFromJsonAsync<ProcessingErrorResponse>();
        Assert.NotNull(responseJson);
        Assert.Equal("ERP_TIMEOUT", responseJson.ErrorCode);
        Assert.Equal(
            "ERP did not respond within 1 second",
            responseJson.Message);
    }

    [Fact]
    public async Task ErpServerError_RetriesThreeTimesAndReturnsBadGateway()
    {
        var key = NewKey("server-error");
        _erpHandler.SetBehavior(key, FakeErpBehavior.ServerError);

        using var response = await SendOrderAsync(key, "SO-TEST-500");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(3, _erpHandler.GetCallCount(key));

        var responseJson = await response.Content.ReadFromJsonAsync<ProcessingErrorResponse>();
        Assert.NotNull(responseJson);
        Assert.Equal("ERP_SERVER_ERROR", responseJson.ErrorCode);
    }

    [Fact]
    public async Task ErpSlowResponse_CompletesBeforeTimeoutWithoutRetry()
    {
        var key = NewKey("slow");
        _erpHandler.SetBehavior(key, FakeErpBehavior.SlowResponse);

        using var response = await SendOrderAsync(key, "SO-TEST-SLOW");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _erpHandler.GetCallCount(key));
    }

    [Fact]
    public async Task ErpPartialSuccess_ReturnsItemLevelDetailsWithoutRetry()
    {
        var key = NewKey("partial");
        _erpHandler.SetBehavior(key, FakeErpBehavior.PartialSuccess);

        using var response = await SendOrderAsync(key, "SO-TEST-PARTIAL");

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        Assert.Equal(1, _erpHandler.GetCallCount(key));

        var responseJson = await response.Content.ReadFromJsonAsync<ProcessingPartialSuccessResponse>();
        Assert.NotNull(responseJson);
        Assert.Equal("ERP_PARTIAL_SUCCESS", responseJson.ErrorCode);
        Assert.Single(responseJson.AcceptedItems);
        Assert.Single(responseJson.RejectedItems);
    }

    private async Task<HttpResponseMessage> SendOrderAsync(
        string idempotencyKey,
        string orderNumber,
        string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/erp/inbound/order")
        {
            Content = JsonContent.Create(CreateOrder(idempotencyKey, orderNumber))
        };

        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        }

        return await _client.SendAsync(request);
    }

    private static object CreateOrder(
        string idempotencyKey,
        string orderNumber,
        int quantity = 2,
        decimal price = 50) =>
        new
        {
            idempotencyKey,
            customer = new
            {
                erpCustomerId = "C1001",
                name = "John Doe",
                email = "john@example.com"
            },
            order = new
            {
                orderNumber,
                date = "2026-09-21",
                lines = new object[]
                {
                    new { sku = "P100", qty = quantity, price },
                    new { sku = "P200", qty = 1, price = 100m }
                }
            }
        };

    private static string NewKey(string scenario) =>
        $"test-{scenario}-{Guid.NewGuid():N}";
}
