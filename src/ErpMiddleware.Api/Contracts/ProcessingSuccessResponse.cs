namespace ErpMiddleware.Api.Contracts;

public sealed record ProcessingSuccessResponse(
    string Status,
    string ErpOrderId,
    string Message);
