using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MockErp.Api.Contracts;

namespace MockErp.Api.Idempotency;

public sealed class OutboundRequestHasher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string ComputeHash(OutboundOrderRequest request)
    {
        var json = JsonSerializer.Serialize(request, SerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));

        return Convert.ToHexString(hash);
    }
}
