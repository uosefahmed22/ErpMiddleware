namespace MockErp.Api.Contracts;

public sealed record MockErpErrorResponse(
    string Status,
    string ErrorCode,
    string Message);
