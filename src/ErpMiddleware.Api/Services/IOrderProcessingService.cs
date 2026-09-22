using ErpMiddleware.Api.Contracts;

namespace ErpMiddleware.Api.Services;

public interface IOrderProcessingService
{
    Task<OrderProcessingResult> ProcessAsync(
        InboundOrderRequest request,
        string correlationId,
        string? simulationMode,
        CancellationToken cancellationToken);
}
