using Arterra.Game.Validators;
using System.Text.Json;
using FluentValidation;
using Arterra.Game.Schemas;

namespace Arterra.Discovery.Transport;

public static class DiscoveryProtocol {
    public const int Version = 1;
    public const int MaximumMessageBytes = 1024;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly DiscoveryMessageValidator Validator = new();

    public static byte[] Encode(DiscoveryServerMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, Json);
    public static DiscoveryClientMessage Decode(byte[] bytes) {
        var message = JsonSerializer.Deserialize<DiscoveryClientMessage>(bytes, Json)
            ?? throw new JsonException("A discovery message is required.");
        Validator.ValidateAndThrow(message);
        return message;
    }
}
