namespace MockErp.Api.Simulation;

public static class ErpSimulationModes
{
    public const string HeaderName = "X-ERP-Simulation";
    public const string Timeout = "timeout";
    public const string ServerError = "server-error";
    public const string SlowResponse = "slow-response";
    public const string PartialSuccess = "partial-success";
}
