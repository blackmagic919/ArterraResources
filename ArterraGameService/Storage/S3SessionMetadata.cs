using Arterra.Game.Validators;
using Arterra.Game.Handlers;
using Arterra.Game.Schemas;
using System.Net;
using Amazon.S3;
using Amazon.S3.Model;

namespace Arterra.Game.Storage;

public sealed class S3SessionMetadata(IAmazonS3 storage, string bucket) : ISessionMetadata {
    public async Task<(UploadTarget PreviewInfo, UploadTarget DetailedInfo)> CreateUploadsAsync(
        Guid id, long previewBytes, long detailedBytes, DateTimeOffset expiresAt) => (
        await UploadAsync($"uploads/{id}/preview-info", "application/octet-stream", previewBytes, expiresAt),
        await UploadAsync($"uploads/{id}/detailed-info", "application/octet-stream", detailedBytes, expiresAt));

    public async Task PublishAsync(Guid id, CancellationToken cancel) {
        try {
            var preview = await storage.GetObjectMetadataAsync(bucket, $"uploads/{id}/preview-info", cancel);
            if (preview.ContentLength is < 0 or > CreateSessionValidator.MaximumPreviewInfoBytes)
                throw new SessionRequestException(400, "Preview info exceeds its permitted size.");

            var detailed = await storage.GetObjectMetadataAsync(bucket, $"uploads/{id}/detailed-info", cancel);
            if (detailed.ContentLength is < 0 or > CreateSessionValidator.MaximumDetailedInfoBytes)
                throw new SessionRequestException(400, "Detailed info exceeds its permitted size.");

            // Publish the exact objects checked above; their contents belong to the client.
            await CopyAsync(id, "preview-info", preview.ETag, cancel);
            await CopyAsync(id, "detailed-info", detailed.ETag, cancel);
        } catch (AmazonS3Exception exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed) {
            throw new SessionRequestException(409, "An upload is missing or changed during completion. Upload the session files and retry.");
        }
    }

    public async Task<(string PreviewInfo, string DetailedInfo)> GetDownloadsAsync(Guid id, DateTimeOffset expiresAt) => (
        await storage.GetPreSignedURLAsync(new GetPreSignedUrlRequest {
            BucketName = bucket, Key = $"sessions/{id}/preview-info", Verb = HttpVerb.GET, Expires = expiresAt.UtcDateTime,
        }),
        await storage.GetPreSignedURLAsync(new GetPreSignedUrlRequest {
            BucketName = bucket, Key = $"sessions/{id}/detailed-info", Verb = HttpVerb.GET, Expires = expiresAt.UtcDateTime,
        }));

    private async Task<UploadTarget> UploadAsync(string key, string type, long length, DateTimeOffset expiresAt) {
        var request = new GetPreSignedUrlRequest {
            BucketName = bucket, Key = key, Verb = HttpVerb.PUT, Expires = expiresAt.UtcDateTime, ContentType = type,
        };
        request.Headers.ContentLength = length;
        return new UploadTarget(await storage.GetPreSignedURLAsync(request), type, length);
    }

    private Task CopyAsync(Guid id, string file, string etag, CancellationToken cancel) => storage.CopyObjectAsync(new CopyObjectRequest {
        SourceBucket = bucket, SourceKey = $"uploads/{id}/{file}",
        DestinationBucket = bucket, DestinationKey = $"sessions/{id}/{file}",
        ETagToMatch = etag,
    }, cancel);
}
