using Arterra.Game.Schemas;

namespace Arterra.Game.Storage;

public interface ISessionStore {
    Task CreateAsync(SessionRecord session, CancellationToken cancellationToken);
    Task<bool> BeginPublicationAsync(Guid sessionId, Guid ownerId, CancellationToken cancellationToken);
    Task FinishPublicationAsync(Guid sessionId, bool success, CancellationToken cancellationToken);
    Task<SessionRecord?> GetAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<SessionPage> ListAsync(int limit, string? cursor, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid sessionId, Guid ownerId, CancellationToken cancellationToken);
}
