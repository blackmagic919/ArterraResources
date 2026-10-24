using Arterra.Game.Schemas;
namespace Arterra.Game.Storage;

public interface ILoginTokenStore {
    Task SaveAsync(string tokenHash, AuthenticatedUser user, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> FindUserAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken);
}
