using Arterra.Game.Schemas;
using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Arterra.Game.Management.Sessions;

namespace Arterra.Game.Management.Storage;

public sealed class DynamoSessionStore(IAmazonDynamoDB database, string table, string metadataBucket, TimeProvider clock) : ISessionStore, ISessionPresence {
    public static readonly TimeSpan HostLease = TimeSpan.FromSeconds(90);

    public async Task CreateAsync(SessionRecord session, CancellationToken cancellationToken) {
        await database.PutItemAsync(new PutItemRequest {
            TableName = table,
            Item = new() {
                ["sessionId"] = new AttributeValue(session.SessionId.ToString()),
                ["ownerId"] = new AttributeValue(session.OwnerId.ToString()),
                ["name"] = new AttributeValue(session.Name),
                ["status"] = new AttributeValue("uploading"),
                ["metadataBucket"] = new AttributeValue(metadataBucket),
                ["metadataPrefix"] = new AttributeValue($"sessions/{session.SessionId}/"),
                ["expiresAt"] = Number(session.ExpiresAt.ToUnixTimeSeconds()),
            },
            ConditionExpression = "attribute_not_exists(sessionId)",
        }, cancellationToken);
    }

    public async Task<bool> BeginPublicationAsync(Guid sessionId, Guid ownerId, CancellationToken cancellationToken) {
        var now = clock.GetUtcNow();
        try {
            await database.UpdateItemAsync(new UpdateItemRequest {
                TableName = table,
                Key = Key(sessionId),
                UpdateExpression = "SET #status = :publishing, expiresAt = :expiry",
                ConditionExpression = "ownerId = :owner AND #status = :uploading AND expiresAt > :now",
                ExpressionAttributeNames = new() { ["#status"] = "status" },
                ExpressionAttributeValues = new() {
                    [":owner"] = new AttributeValue(ownerId.ToString()),
                    [":uploading"] = new AttributeValue("uploading"),
                    [":publishing"] = new AttributeValue("publishing"),
                    [":now"] = Number(now.ToUnixTimeSeconds()),
                    [":expiry"] = Number((now + TimeSpan.FromHours(1)).ToUnixTimeSeconds()),
                },
            }, cancellationToken);
            return true;
        } catch (ConditionalCheckFailedException) { return false; }
    }

    public async Task FinishPublicationAsync(Guid sessionId, bool success, CancellationToken cancellationToken) {
        await database.UpdateItemAsync(new UpdateItemRequest {
            TableName = table,
            Key = Key(sessionId),
            UpdateExpression = "SET #status = :next",
            ConditionExpression = "#status = :publishing",
            ExpressionAttributeNames = new() { ["#status"] = "status" },
            ExpressionAttributeValues = new() {
                [":publishing"] = new AttributeValue("publishing"),
                [":next"] = new AttributeValue(success ? "ready" : "uploading"),
            },
        }, cancellationToken);
    }

    public async Task<SessionRecord?> GetAsync(Guid sessionId, CancellationToken cancellationToken) {
        var result = await database.GetItemAsync(new GetItemRequest {
            TableName = table,
            Key = Key(sessionId),
            ConsistentRead = true,
        }, cancellationToken);
        return result.Item == null || result.Item.Count == 0 ? null : Read(result.Item);
    }

    public async Task<SessionPage> ListAsync(int limit, Guid? cursor, CancellationToken cancellationToken) {
        var result = await database.ScanAsync(new ScanRequest {
            TableName = table,
            Limit = limit,
            ExclusiveStartKey = cursor.HasValue ? Key(cursor.Value) : null,
            FilterExpression = "#status = :active AND expiresAt > :now",
            ExpressionAttributeNames = new() { ["#status"] = "status" },
            ExpressionAttributeValues = new() {
                [":active"] = new AttributeValue("active"),
                [":now"] = Number(clock.GetUtcNow().ToUnixTimeSeconds()),
            },
        }, cancellationToken);

        var next = result.LastEvaluatedKey;
        return new SessionPage((result.Items ?? []).Select(Read).ToList(),
            next != null && next.Count != 0 ? Guid.Parse(next["sessionId"].S) : null);
    }

    public async Task<bool> DeleteAsync(Guid sessionId, Guid ownerId, CancellationToken cancellationToken) {
        try {
            await database.DeleteItemAsync(new DeleteItemRequest {
                TableName = table,
                Key = Key(sessionId),
                ConditionExpression = "ownerId = :owner AND #status <> :publishing",
                ExpressionAttributeNames = new() { ["#status"] = "status" },
                ExpressionAttributeValues = new() {
                    [":owner"] = new AttributeValue(ownerId.ToString()),
                    [":publishing"] = new AttributeValue("publishing"),
                },
            }, cancellationToken);
            return true;
        } catch (ConditionalCheckFailedException) { return false; }
    }

    public async Task<bool> RenewAsync(Guid sessionId, Guid ownerId, NetworkEndpoint observedHost, CancellationToken cancellationToken) {
        if (!System.Net.IPAddress.TryParse(observedHost.Host, out _) || observedHost.Port is < 1 or > 65535)
            throw new ArgumentException("A valid observed UDP endpoint is required.");

        var now = clock.GetUtcNow();
        try {
            await database.UpdateItemAsync(new UpdateItemRequest {
                TableName = table,
                Key = Key(sessionId),
                UpdateExpression = "SET #status = :active, expiresAt = :expiry, hostIp = :ip, hostPort = :port",
                ConditionExpression = "ownerId = :owner AND (#status = :ready OR #status = :active) AND expiresAt > :now",
                ExpressionAttributeNames = new() { ["#status"] = "status" },
                ExpressionAttributeValues = new() {
                    [":owner"] = new AttributeValue(ownerId.ToString()),
                    [":active"] = new AttributeValue("active"),
                    [":ready"] = new AttributeValue("ready"),
                    [":expiry"] = Number((now + HostLease).ToUnixTimeSeconds()),
                    [":now"] = Number(now.ToUnixTimeSeconds()),
                    [":ip"] = new AttributeValue(observedHost.Host),
                    [":port"] = Number(observedHost.Port),
                },
            }, cancellationToken);
            return true;
        } catch (ConditionalCheckFailedException) { return false; }
    }

    private static Dictionary<string, AttributeValue> Key(Guid id) => new() { ["sessionId"] = new AttributeValue(id.ToString()) };
    private static AttributeValue Number(long value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };

    private static SessionRecord Read(Dictionary<string, AttributeValue> item) => new(
        Guid.Parse(item["sessionId"].S), Guid.Parse(item["ownerId"].S), item["name"].S,
        item["status"].S, DateTimeOffset.FromUnixTimeSeconds(long.Parse(item["expiresAt"].N, CultureInfo.InvariantCulture)),
        item.TryGetValue("hostIp", out var ip) ? new NetworkEndpoint(ip.S, int.Parse(item["hostPort"].N, CultureInfo.InvariantCulture)) : null);
}
