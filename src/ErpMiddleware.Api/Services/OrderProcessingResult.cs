namespace ErpMiddleware.Api.Services;

public sealed record OrderProcessingResult(
    OrderProcessingOutcome Outcome,
    object Response,
    bool IsReplay = false);

public enum OrderProcessingOutcome
{
    Success,
    PartialSuccess,
    Conflict,
    Timeout,
    UpstreamFailure
}
