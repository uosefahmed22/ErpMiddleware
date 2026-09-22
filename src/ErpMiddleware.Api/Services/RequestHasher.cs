using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErpMiddleware.Api.Contracts;

namespace ErpMiddleware.Api.Services;

public sealed class RequestHasher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string ComputeHash(InboundOrderRequest request)
    {
        var json = JsonSerializer.Serialize(request, SerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));

        return Convert.ToHexString(hash);
    }
}
