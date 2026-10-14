using Arterra.Game.Schemas;

namespace Arterra.Game.Management.Sessions;

// Only trusted connectivity code calls this boundary. There is no public HTTP heartbeat endpoint.
// The endpoint must come from the observed game UDP connection, not its REST source port.
public interface ISessionPresence {
    Task<bool> RenewAsync(Guid sessionId, Guid ownerId, NetworkEndpoint observedHost, CancellationToken cancellationToken);
}

public interface ISessionStore {
    Task CreateAsync(SessionRecord session, CancellationToken cancellationToken);
    Task<bool> BeginPublicationAsync(Guid sessionId, Guid ownerId, CancellationToken cancellationToken);
    Task FinishPublicationAsync(Guid sessionId, bool success, CancellationToken cancellationToken);
    Task<SessionRecord?> GetAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<SessionPage> ListAsync(int limit, Guid? cursor, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid sessionId, Guid ownerId, CancellationToken cancellationToken);
}

public interface ISessionMetadata {
    Task<(UploadTarget Metadata, UploadTarget DisplayChunk)> CreateUploadsAsync(Guid sessionId,
        long metadataBytes, long chunkBytes, DateTimeOffset expiresAt);
    Task PublishAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<(string Metadata, string DisplayChunk)> GetDownloadsAsync(Guid sessionId, DateTimeOffset expiresAt);
}

public sealed class SessionRequestException(int statusCode, string message) : Exception(message) {
    public int StatusCode { get; } = statusCode;
}
