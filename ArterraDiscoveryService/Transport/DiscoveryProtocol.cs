using System.Text.Json;
using Arterra.Game.Schemas;

namespace Arterra.Discovery.Transport;

public static class DiscoveryProtocol {
    public const int Version = 1;
    public const int MaximumMessageBytes = 1024;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static byte[] Encode(DiscoveryServerMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, Json);
    public static DiscoveryClientMessage? Decode(byte[] bytes) => JsonSerializer.Deserialize<DiscoveryClientMessage>(bytes, Json);
}
