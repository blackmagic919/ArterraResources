using Arterra.Game.Schemas;
using System.Net;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Arterra.Game.Management.Sessions;

namespace Arterra.Game.Management.Storage;

public sealed class S3SessionMetadata(IAmazonS3 storage, string bucket) : ISessionMetadata {
    public async Task<(UploadTarget Metadata, UploadTarget DisplayChunk)> CreateUploadsAsync(
        Guid id, long metadataBytes, long chunkBytes, DateTimeOffset expiresAt) {
        var metadata = await UploadAsync($"uploads/{id}/metadata.json", "application/json", metadataBytes, expiresAt);
        var chunk = await UploadAsync($"uploads/{id}/display-chunk.bin", "application/octet-stream", chunkBytes, expiresAt);
        return (metadata, chunk);
    }

    public async Task PublishAsync(Guid id, CancellationToken cancel) {
        try {
            using var metadata = await storage.GetObjectAsync(bucket, $"uploads/{id}/metadata.json", cancel);
            if (metadata.ContentLength is < 2 or > SessionService.MaximumMetadataBytes)
                throw new SessionRequestException(400, "Metadata exceeds its permitted size.");

            try {
                using var json = await JsonDocument.ParseAsync(metadata.ResponseStream, cancellationToken: cancel);
                if (json.RootElement.ValueKind != JsonValueKind.Object)
                    throw new SessionRequestException(400, "Metadata must be a JSON object.");
            } catch (JsonException) {
                throw new SessionRequestException(400, "Metadata must be a valid JSON object.");
            }

            var chunk = await storage.GetObjectMetadataAsync(bucket, $"uploads/{id}/display-chunk.bin", cancel);
            if (chunk.ContentLength is < 1 or > SessionService.MaximumChunkBytes)
                throw new SessionRequestException(400, "Display chunk exceeds its permitted size.");

            // Copy only the exact objects just verified. Upload URLs can never overwrite published objects.
            await CopyAsync(id, "metadata.json", metadata.ETag, cancel);
            await CopyAsync(id, "display-chunk.bin", chunk.ETag, cancel);
        } catch (AmazonS3Exception exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed) {
            throw new SessionRequestException(409, "An upload is missing or changed during completion. Upload both files and retry.");
        }
    }

    public async Task<(string Metadata, string DisplayChunk)> GetDownloadsAsync(Guid id, DateTimeOffset expiresAt) => (
        await storage.GetPreSignedURLAsync(new GetPreSignedUrlRequest {
            BucketName = bucket, Key = $"sessions/{id}/metadata.json", Verb = HttpVerb.GET, Expires = expiresAt.UtcDateTime,
        }),
        await storage.GetPreSignedURLAsync(new GetPreSignedUrlRequest {
            BucketName = bucket, Key = $"sessions/{id}/display-chunk.bin", Verb = HttpVerb.GET, Expires = expiresAt.UtcDateTime,
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
