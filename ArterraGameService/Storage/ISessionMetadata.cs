using Arterra.Game.Schemas;

namespace Arterra.Game.Storage;

public interface ISessionMetadata {
    Task<(UploadTarget Metadata, UploadTarget DisplayChunk)> CreateUploadsAsync(Guid sessionId,
        long metadataBytes, long chunkBytes, DateTimeOffset expiresAt);
    Task PublishAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<(string Metadata, string DisplayChunk)> GetDownloadsAsync(Guid sessionId, DateTimeOffset expiresAt);
}
