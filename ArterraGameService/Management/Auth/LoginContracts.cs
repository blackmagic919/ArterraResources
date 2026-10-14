using System.Text.Json;
using Arterra.Game.Schemas;

namespace Arterra.Game.Management.Auth;

public interface IIdentityProvider {
    string Name { get; }
    Task<ExternalIdentity> ValidateAsync(JsonElement credential, CancellationToken cancellationToken);
}

public interface IIdentityStore {
    Task<Guid> GetOrCreateUserAsync(ExternalIdentity identity, CancellationToken cancellationToken);
}

public interface ILoginTokenStore {
    Task SaveAsync(string tokenHash, Guid userId, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<Guid?> FindUserAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken);
}
