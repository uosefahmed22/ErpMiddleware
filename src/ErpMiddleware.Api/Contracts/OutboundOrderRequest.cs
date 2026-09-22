namespace ErpMiddleware.Api.Contracts;

public sealed record OutboundOrderRequest(
    string CustomerId,
    string FullName,
    string OrderCode,
    decimal TotalAmount,
    IReadOnlyList<OutboundOrderItem> Items);

public sealed record OutboundOrderItem(string Code, int Quantity);
