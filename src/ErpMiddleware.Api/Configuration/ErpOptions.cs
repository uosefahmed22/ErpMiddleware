namespace ErpMiddleware.Api.Configuration;

public sealed class ErpOptions
{
    public const string SectionName = "Erp";

    public string BaseUrl { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 10;

    public int MaxAttempts { get; init; } = 3;

    public int InitialBackoffMilliseconds { get; init; } = 1000;
}
