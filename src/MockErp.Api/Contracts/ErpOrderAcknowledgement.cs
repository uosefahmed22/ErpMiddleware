namespace MockErp.Api.Contracts;

public sealed record ErpOrderAcknowledgement(
    string Status,
    string ErpOrderId,
    string Message,
    IReadOnlyList<string>? AcceptedItems = null,
    IReadOnlyList<RejectedErpItem>? RejectedItems = null);

public sealed record RejectedErpItem(
    string Code,
    string Reason);
