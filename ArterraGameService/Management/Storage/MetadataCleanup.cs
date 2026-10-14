using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.S3;
using Amazon.S3.Model;

namespace Arterra.Game.Management.Storage;

// A separate Lambda entry point in the same deployment artifact. No HTTP/auth startup runs here.
public sealed class MetadataCleanup {
    private readonly IAmazonS3 storage = new AmazonS3Client();
    private readonly string bucket = Environment.GetEnvironmentVariable("METADATA_BUCKET")
        ?? throw new InvalidOperationException("METADATA_BUCKET is required.");

    [LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]
    public async Task HandleAsync(DynamoDBEvent batch, ILambdaContext context) {
        foreach (var record in batch.Records) {
            if (record.EventName != "REMOVE") continue;
            var id = Guid.Parse(record.Dynamodb.Keys["sessionId"].S);
            var result = await storage.DeleteObjectsAsync(new DeleteObjectsRequest {
                BucketName = bucket,
                Objects = [
                    new KeyVersion { Key = $"sessions/{id}/metadata.json" },
                    new KeyVersion { Key = $"sessions/{id}/display-chunk.bin" },
                ],
            });
            if (result.DeleteErrors?.Count > 0)
                throw new IOException($"Could not remove metadata for session {id}.");
        }
    }
}
