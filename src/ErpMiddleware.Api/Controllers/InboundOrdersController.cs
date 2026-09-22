using ErpMiddleware.Api.Contracts;
using ErpMiddleware.Api.Services;
using ErpMiddleware.Api.Simulation;
using Microsoft.AspNetCore.Mvc;

namespace ErpMiddleware.Api.Controllers;

[ApiController]
[Route("erp/inbound/order")]
public sealed class InboundOrdersController(IOrderProcessingService orderProcessingService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ProcessingSuccessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProcessingErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProcessingPartialSuccessResponse>(StatusCodes.Status207MultiStatus)]
    [ProducesResponseType<ProcessingErrorResponse>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProcessingErrorResponse>(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> Create(
        [FromBody] InboundOrderRequest request,
        [FromHeader(Name = ErpSimulationModes.HeaderName)] string? simulationMode,
        CancellationToken cancellationToken)
    {
        if (!ErpSimulationModes.TryNormalize(simulationMode, out var normalizedSimulationMode))
        {
            return BadRequest(new ProcessingErrorResponse(
                "error",
                "INVALID_SIMULATION_MODE",
                "X-ERP-Simulation must be timeout, server-error, slow-response, or partial-success."));
        }

        var result = await orderProcessingService.ProcessAsync(
            request,
            HttpContext.TraceIdentifier,
            normalizedSimulationMode,
            cancellationToken);

        if (result.IsReplay)
        {
            Response.Headers["Idempotency-Replayed"] = "true";
        }

        var statusCode = result.Outcome switch
        {
            OrderProcessingOutcome.Success => StatusCodes.Status200OK,
            OrderProcessingOutcome.PartialSuccess => StatusCodes.Status207MultiStatus,
            OrderProcessingOutcome.Conflict => StatusCodes.Status409Conflict,
            OrderProcessingOutcome.Timeout => StatusCodes.Status504GatewayTimeout,
            OrderProcessingOutcome.UpstreamFailure => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status500InternalServerError
        };

        return StatusCode(statusCode, result.Response);
    }
}
