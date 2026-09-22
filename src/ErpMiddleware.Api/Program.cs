using ErpMiddleware.Api.Clients;
using ErpMiddleware.Api.Configuration;
using ErpMiddleware.Api.Middleware;
using ErpMiddleware.Api.Services;
using ErpMiddleware.Api.Storage;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services
    .AddOptions<ErpOptions>()
    .BindConfiguration(ErpOptions.SectionName)
    .Validate(
        options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
        "Erp:BaseUrl must be an absolute URL.")
    .Validate(
        options => options.TimeoutSeconds is > 0 and <= 300,
        "Erp:TimeoutSeconds must be between 1 and 300.")
    .Validate(
        options => options.MaxAttempts is > 0 and <= 10,
        "Erp:MaxAttempts must be between 1 and 10.")
    .Validate(
        options => options.InitialBackoffMilliseconds is >= 0 and <= 60000,
        "Erp:InitialBackoffMilliseconds must be between 0 and 60000.")
    .ValidateOnStart();

builder.Services.AddSingleton<IInboundRequestStore, InMemoryInboundRequestStore>();
builder.Services.AddSingleton<OrderTransformer>();
builder.Services.AddSingleton<RequestHasher>();
builder.Services.AddScoped<IOrderProcessingService, OrderProcessingService>();

builder.Services.AddHttpClient<IErpOrderClient, ErpOrderClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ErpOptions>>().Value;

    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = Timeout.InfiniteTimeSpan;
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();