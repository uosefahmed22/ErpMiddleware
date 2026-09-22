using ErpMiddleware.Api.Contracts;

namespace ErpMiddleware.Api.Storage;

public sealed class InMemoryInboundRequestStore : IInboundRequestStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, StoredInboundRequest> _requests = new(StringComparer.Ordinal);

    public IdempotencyDecision TryBegin(
        InboundOrderRequest request,
        string requestHash,
        DateTimeOffset receivedAt)
    {
        lock (_sync)
        {
            if (!_requests.TryGetValue(request.IdempotencyKey, out var existing))
            {
                _requests.Add(
                    request.IdempotencyKey,
                    new StoredInboundRequest(requestHash, request, receivedAt));

                return new IdempotencyDecision(IdempotencyDecisionType.Started);
            }

            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return new IdempotencyDecision(IdempotencyDecisionType.Conflict);
            }

            if (existing.Result is not null)
            {
                return new IdempotencyDecision(
                    IdempotencyDecisionType.Replay,
                    existing.Result);
            }

            return new IdempotencyDecision(IdempotencyDecisionType.InProgress);
        }
    }

    public void Complete(string idempotencyKey, StoredProcessingResult result)
    {
        lock (_sync)
        {
            if (_requests.TryGetValue(idempotencyKey, out var existing))
            {
                existing.Result = result;
            }
        }
    }

    public void Remove(string idempotencyKey)
    {
        lock (_sync)
        {
            _requests.Remove(idempotencyKey);
        }
    }

    private sealed class StoredInboundRequest(
        string requestHash,
        InboundOrderRequest request,
        DateTimeOffset receivedAt)
    {
        public string RequestHash { get; } = requestHash;

        public InboundOrderRequest Request { get; } = request;

        public DateTimeOffset ReceivedAt { get; } = receivedAt;

        public StoredProcessingResult? Result { get; set; }
    }
}
