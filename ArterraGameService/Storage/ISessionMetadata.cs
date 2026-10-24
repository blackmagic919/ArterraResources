using Arterra.Game.Schemas;

namespace Arterra.Game.Storage;

public interface ISessionMetadata {
    Task<(UploadTarget PreviewInfo, UploadTarget DetailedInfo)> CreateUploadsAsync(Guid sessionId,
        long previewBytes, long detailedBytes, DateTimeOffset expiresAt);
    Task PublishAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<(string PreviewInfo, string DetailedInfo)> GetDownloadsAsync(Guid sessionId, DateTimeOffset expiresAt);
}
