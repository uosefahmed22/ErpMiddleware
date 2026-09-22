using MockErp.Api.Contracts;

namespace MockErp.Api.Idempotency;

public sealed class MockErpIdempotencyStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, StoredRequest> _requests = new(StringComparer.Ordinal);

    public MockErpIdempotencyDecision TryBegin(string idempotencyKey, string requestHash)
    {
        lock (_sync)
        {
            if (!_requests.TryGetValue(idempotencyKey, out var existing))
            {
                _requests.Add(idempotencyKey, new StoredRequest(requestHash));
                return new MockErpIdempotencyDecision(MockErpIdempotencyDecisionType.Started);
            }

            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return new MockErpIdempotencyDecision(MockErpIdempotencyDecisionType.Conflict);
            }

            if (existing.Response is not null)
            {
                return new MockErpIdempotencyDecision(
                    MockErpIdempotencyDecisionType.Replay,
                    existing.Response);
            }

            return new MockErpIdempotencyDecision(MockErpIdempotencyDecisionType.InProgress);
        }
    }

    public void Complete(
        string idempotencyKey,
        string requestHash,
        StoredMockErpResponse response)
    {
        lock (_sync)
        {
            if (!_requests.TryGetValue(idempotencyKey, out var existing)
                || !string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Cannot complete a mock ERP request that is not currently being processed.");
            }

            existing.Response = response;
        }
    }

    public void Abandon(string idempotencyKey, string requestHash)
    {
        lock (_sync)
        {
            if (_requests.TryGetValue(idempotencyKey, out var existing)
                && existing.Response is null
                && string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                _requests.Remove(idempotencyKey);
            }
        }
    }

    private sealed class StoredRequest(string requestHash)
    {
        public string RequestHash { get; } = requestHash;

        public StoredMockErpResponse? Response { get; set; }
    }
}

public sealed record MockErpIdempotencyDecision(
    MockErpIdempotencyDecisionType Type,
    StoredMockErpResponse? StoredResponse = null);

public sealed record StoredMockErpResponse(
    int StatusCode,
    ErpOrderAcknowledgement Body);

public enum MockErpIdempotencyDecisionType
{
    Started,
    Replay,
    Conflict,
    InProgress
}
