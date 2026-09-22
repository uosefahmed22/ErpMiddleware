namespace ErpMiddleware.Api.Contracts;

public sealed record ProcessingErrorResponse(
    string Status,
    string ErrorCode,
    string Message);
