using ErpMiddleware.Api;
using ErpMiddleware.Api.Clients;
using ErpMiddleware.Api.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ErpMiddleware.Tests.Infrastructure;

public sealed class ErpMiddlewareApplicationFactory : WebApplicationFactory<ErpMiddlewareApiMarker>
{
    public FakeErpHttpMessageHandler ErpHandler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Erp:BaseUrl"] = "http://fake-erp",
                ["Erp:TimeoutSeconds"] = "1",
                ["Erp:MaxAttempts"] = "3",
                ["Erp:InitialBackoffMilliseconds"] = "1"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IErpOrderClient>();

            services
                .AddHttpClient<IErpOrderClient, ErpOrderClient>((serviceProvider, client) =>
                {
                    var options = serviceProvider.GetRequiredService<IOptions<ErpOptions>>().Value;
                    client.BaseAddress = new Uri(options.BaseUrl);
                    client.Timeout = Timeout.InfiniteTimeSpan;
                })
                .ConfigurePrimaryHttpMessageHandler(() => ErpHandler);
        });
    }
}
