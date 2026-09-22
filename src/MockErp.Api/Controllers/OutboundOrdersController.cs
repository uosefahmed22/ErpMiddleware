using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MockErp.Api.Configuration;
using MockErp.Api.Contracts;
using MockErp.Api.Idempotency;
using MockErp.Api.Simulation;

namespace MockErp.Api.Controllers;

[ApiController]
[Route("erp/outbound/order")]
public sealed class OutboundOrdersController(
    IOptions<MockErpSimulationOptions> options,
    MockErpIdempotencyStore idempotencyStore,
    OutboundRequestHasher requestHasher,
    ILogger<OutboundOrdersController> logger) : ControllerBase
{
    private readonly MockErpSimulationOptions _options = options.Value;

    [HttpPost]
    [ProducesResponseType<ErpOrderAcknowledgement>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErpOrderAcknowledgement>(StatusCodes.Status207MultiStatus)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<MockErpErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ErpOrderAcknowledgement>> Create(
        [FromBody] OutboundOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromHeader(Name = "X-Correlation-ID")] string? correlationId,
        [FromHeader(Name = ErpSimulationModes.HeaderName)] string? simulationMode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            ModelState.AddModelError("Idempotency-Key", "Idempotency-Key header is required.");
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            ModelState.AddModelError("X-Correlation-ID", "X-Correlation-ID header is required.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var requestHash = requestHasher.ComputeHash(request);
        var decision = idempotencyStore.TryBegin(idempotencyKey!, requestHash);

        if (decision.Type == MockErpIdempotencyDecisionType.Replay)
        {
            logger.LogInformation(
                "Mock ERP replayed the stored response for idempotency key {IdempotencyKey}",
                idempotencyKey);

            Response.Headers["Idempotency-Replayed"] = "true";

            return StatusCode(
                decision.StoredResponse!.StatusCode,
                decision.StoredResponse.Body);
        }

        if (decision.Type == MockErpIdempotencyDecisionType.Conflict)
        {
            logger.LogWarning(
                "Mock ERP idempotency conflict for key {IdempotencyKey}",
                idempotencyKey);

            return Conflict(new MockErpErrorResponse(
                "error",
                "IDEMPOTENCY_CONFLICT",
                "The idempotency key was already used with a different request payload."));
        }

        if (decision.Type == MockErpIdempotencyDecisionType.InProgress)
        {
            logger.LogWarning(
                "Mock ERP request with idempotency key {IdempotencyKey} is already processing",
                idempotencyKey);

            return Conflict(new MockErpErrorResponse(
                "error",
                "REQUEST_IN_PROGRESS",
                "A request with this idempotency key is already being processed."));
        }

        var completed = false;

        try
        {
            logger.LogInformation(
                "Mock ERP received order {OrderCode} for customer {CustomerId}. Total {TotalAmount}, items {ItemCount}, idempotency key {IdempotencyKey}, correlation ID {CorrelationId}, simulation {SimulationMode}",
                request.OrderCode,
                request.CustomerId,
                request.TotalAmount,
                request.Items!.Count,
                idempotencyKey,
                correlationId,
                simulationMode ?? "none");

            switch (simulationMode?.Trim().ToLowerInvariant())
            {
                case ErpSimulationModes.Timeout:
                    logger.LogWarning(
                        "Simulating ERP timeout for order {OrderCode} with a delay of {DelayMilliseconds} ms",
                        request.OrderCode,
                        _options.TimeoutDelayMilliseconds);

                    await Task.Delay(
                        TimeSpan.FromMilliseconds(_options.TimeoutDelayMilliseconds),
                        cancellationToken);
                    break;

                case ErpSimulationModes.ServerError:
                    logger.LogWarning("Simulating ERP 500 error for order {OrderCode}", request.OrderCode);
                    return StatusCode(
                        StatusCodes.Status500InternalServerError,
                        new MockErpErrorResponse(
                            "error",
                            "SIMULATED_ERP_ERROR",
                            "The mock ERP returned a simulated server error."));

                case ErpSimulationModes.SlowResponse:
                    logger.LogWarning(
                        "Simulating slow ERP response for order {OrderCode} with a delay of {DelayMilliseconds} ms",
                        request.OrderCode,
                        _options.SlowResponseDelayMilliseconds);

                    await Task.Delay(
                        TimeSpan.FromMilliseconds(_options.SlowResponseDelayMilliseconds),
                        cancellationToken);
                    break;

                case ErpSimulationModes.PartialSuccess:
                    logger.LogWarning("Simulating ERP partial success for order {OrderCode}", request.OrderCode);

                    var partialResponse = CreatePartialSuccessResponse(request);
                    idempotencyStore.Complete(
                        idempotencyKey!,
                        requestHash,
                        new StoredMockErpResponse(
                            StatusCodes.Status207MultiStatus,
                            partialResponse));
                    completed = true;

                    return StatusCode(StatusCodes.Status207MultiStatus, partialResponse);
            }

            logger.LogInformation(
                "Mock ERP accepted order {OrderCode} with status {StatusCode}",
                request.OrderCode,
                StatusCodes.Status200OK);

            var successResponse = new ErpOrderAcknowledgement(
                "success",
                request.OrderCode,
                "Order accepted by ERP");

            idempotencyStore.Complete(
                idempotencyKey!,
                requestHash,
                new StoredMockErpResponse(StatusCodes.Status200OK, successResponse));
            completed = true;

            return Ok(successResponse);
        }
        finally
        {
            if (!completed)
            {
                idempotencyStore.Abandon(idempotencyKey!, requestHash);
            }
        }
    }

    private static ErpOrderAcknowledgement CreatePartialSuccessResponse(OutboundOrderRequest request)
    {
        var acceptedCount = request.Items!.Count / 2;
        var acceptedItems = request.Items
            .Take(acceptedCount)
            .Select(item => item.Code)
            .ToArray();

        var rejectedItems = request.Items
            .Skip(acceptedCount)
            .Select(item => new RejectedErpItem(
                item.Code,
                "Simulated item rejection"))
            .ToArray();

        return new ErpOrderAcknowledgement(
            "partial_success",
            request.OrderCode,
            "ERP processed some order items and rejected others",
            acceptedItems,
            rejectedItems);
    }
}
