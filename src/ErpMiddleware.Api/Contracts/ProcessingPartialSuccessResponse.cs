namespace ErpMiddleware.Api.Contracts;

public sealed record ProcessingPartialSuccessResponse(
    string Status,
    string ErpOrderId,
    string ErrorCode,
    string Message,
    IReadOnlyList<string> AcceptedItems,
    IReadOnlyList<RejectedOrderItemResponse> RejectedItems);

public sealed record RejectedOrderItemResponse(
    string Code,
    string Reason);
