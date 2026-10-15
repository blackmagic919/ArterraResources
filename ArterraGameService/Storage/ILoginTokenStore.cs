namespace Arterra.Game.Storage;

public interface ILoginTokenStore {
    Task SaveAsync(string tokenHash, Guid userId, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<Guid?> FindUserAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken);
}
