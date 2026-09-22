using MockErp.Api.Configuration;
using MockErp.Api.Idempotency;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<MockErpIdempotencyStore>();
builder.Services.AddSingleton<OutboundRequestHasher>();

builder.Services
    .AddOptions<MockErpSimulationOptions>()
    .BindConfiguration(MockErpSimulationOptions.SectionName)
    .Validate(
        options => options.SlowResponseDelayMilliseconds >= 0,
        "MockErpSimulation:SlowResponseDelayMilliseconds cannot be negative.")
    .Validate(
        options => options.TimeoutDelayMilliseconds >= 0,
        "MockErpSimulation:TimeoutDelayMilliseconds cannot be negative.")
    .ValidateOnStart();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();
