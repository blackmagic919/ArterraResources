using Arterra.Game.Schemas;

namespace Arterra.Game.Storage;

public interface IIdentityStore {
    Task<Guid> GetOrCreateUserAsync(ExternalIdentity identity, CancellationToken cancellationToken);
}
