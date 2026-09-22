namespace ErpMiddleware.Api.Simulation;

public static class ErpSimulationModes
{
    public const string HeaderName = "X-ERP-Simulation";
    public const string Timeout = "timeout";
    public const string ServerError = "server-error";
    public const string SlowResponse = "slow-response";
    public const string PartialSuccess = "partial-success";

    private static readonly HashSet<string> SupportedModes =
    [
        Timeout,
        ServerError,
        SlowResponse,
        PartialSuccess
    ];

    public static bool TryNormalize(string? value, out string? normalizedMode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalizedMode = null;
            return true;
        }

        normalizedMode = value.Trim().ToLowerInvariant();
        return SupportedModes.Contains(normalizedMode);
    }
}
