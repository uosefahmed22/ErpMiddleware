namespace MockErp.Api.Configuration;

public sealed class MockErpSimulationOptions
{
    public const string SectionName = "MockErpSimulation";

    public int SlowResponseDelayMilliseconds { get; init; } = 2000;

    public int TimeoutDelayMilliseconds { get; init; } = 12000;
}
