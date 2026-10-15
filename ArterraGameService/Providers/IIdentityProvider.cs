using Arterra.Game.Schemas;
using System.Text.Json;

namespace Arterra.Game.Providers;

public interface IIdentityProvider {
    string Name { get; }
    Task<ExternalIdentity> ValidateAsync(JsonElement credential, CancellationToken cancellationToken);
}
