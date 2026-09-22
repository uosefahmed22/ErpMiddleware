using ErpMiddleware.Api.Contracts;

namespace ErpMiddleware.Api.Storage;

public interface IInboundRequestStore
{
    IdempotencyDecision TryBegin(
        InboundOrderRequest request,
        string requestHash,
        DateTimeOffset receivedAt);

    void Complete(string idempotencyKey, StoredProcessingResult result);

    void Remove(string idempotencyKey);
}

public sealed record IdempotencyDecision(
    IdempotencyDecisionType Type,
    StoredProcessingResult? StoredResult = null);

public sealed record StoredProcessingResult(
    Services.OrderProcessingOutcome Outcome,
    object Response);

public enum IdempotencyDecisionType
{
    Started,
    Replay,
    Conflict,
    InProgress
}
