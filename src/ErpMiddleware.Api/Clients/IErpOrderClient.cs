using ErpMiddleware.Api.Contracts;

namespace ErpMiddleware.Api.Clients;

public interface IErpOrderClient
{
    Task<ErpOrderAcknowledgement> SendAsync(
        OutboundOrderRequest request,
        string idempotencyKey,
        string correlationId,
        string? simulationMode,
        CancellationToken cancellationToken);
}

public sealed record ErpOrderAcknowledgement(
    string Status,
    string ErpOrderId,
    string Message,
    IReadOnlyList<string>? AcceptedItems = null,
    IReadOnlyList<RejectedErpItem>? RejectedItems = null);

public sealed record RejectedErpItem(
    string Code,
    string Reason);
