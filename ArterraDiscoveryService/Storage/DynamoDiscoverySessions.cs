using System.Globalization;
using System.Net;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Arterra.Discovery.Storage;

public sealed class DynamoDiscoverySessions(IAmazonDynamoDB database, string table, TimeProvider clock) : IDiscoverySessions {
    public static readonly TimeSpan HostLease = TimeSpan.FromSeconds(90);

    public Task<bool> ActivateAsync(Guid sessionId, Guid ownerId, Guid leaseId, IPEndPoint endpoint, CancellationToken cancel) {
        var values = Values(ownerId, leaseId);
        values[":ready"] = new AttributeValue("ready");
        values[":ip"] = new AttributeValue(endpoint.Address.ToString());
        values[":port"] = Number(endpoint.Port);
        values[":expiry"] = Number((clock.GetUtcNow() + HostLease).ToUnixTimeSeconds());
        return UpdateAsync(sessionId,
            "SET #status = :active, hostLeaseId = :lease, expiresAt = :expiry, hostIp = :ip, hostPort = :port",
            "ownerId = :owner AND #status = :ready AND expiresAt > :now", values, cancel);
    }

    public Task<bool> RenewAsync(Guid sessionId, Guid ownerId, Guid leaseId, CancellationToken cancel) {
        var values = Values(ownerId, leaseId);
        values[":expiry"] = Number((clock.GetUtcNow() + HostLease).ToUnixTimeSeconds());
        return UpdateAsync(sessionId, "SET expiresAt = :expiry",
            "ownerId = :owner AND hostLeaseId = :lease AND #status = :active AND expiresAt > :now", values, cancel);
    }

    public async Task ReleaseAsync(Guid sessionId, Guid ownerId, Guid leaseId, CancellationToken cancel) {
        var values = Values(ownerId, leaseId);
        values.Remove(":now");
        values[":ready"] = new AttributeValue("ready");
        // Preserve the existing expiry so an owner may reconnect within the remaining lease.
        await UpdateAsync(sessionId, "SET #status = :ready REMOVE hostLeaseId, hostIp, hostPort",
            "ownerId = :owner AND hostLeaseId = :lease AND #status = :active", values, cancel);
    }

    public async Task<bool> IsActiveAsync(Guid sessionId, Guid leaseId, CancellationToken cancel) {
        var result = await database.GetItemAsync(new GetItemRequest {
            TableName = table, Key = Key(sessionId), ConsistentRead = true,
        }, cancel);
        var item = result.Item;
        return item != null && item.TryGetValue("status", out var status) && status.S == "active"
            && item.TryGetValue("hostLeaseId", out var lease) && lease.S == leaseId.ToString()
            && long.Parse(item["expiresAt"].N, CultureInfo.InvariantCulture) > clock.GetUtcNow().ToUnixTimeSeconds();
    }

    private async Task<bool> UpdateAsync(Guid sessionId, string update, string condition,
        Dictionary<string, AttributeValue> values, CancellationToken cancel) {
        try {
            await database.UpdateItemAsync(new UpdateItemRequest {
                TableName = table, Key = Key(sessionId), UpdateExpression = update, ConditionExpression = condition,
                ExpressionAttributeNames = new() { ["#status"] = "status" }, ExpressionAttributeValues = values,
            }, cancel);
            return true;
        } catch (ConditionalCheckFailedException) { return false; }
    }

    private Dictionary<string, AttributeValue> Values(Guid ownerId, Guid leaseId) => new() {
        [":owner"] = new AttributeValue(ownerId.ToString()),
        [":lease"] = new AttributeValue(leaseId.ToString()),
        [":active"] = new AttributeValue("active"),
        [":now"] = Number(clock.GetUtcNow().ToUnixTimeSeconds()),
    };

    private static Dictionary<string, AttributeValue> Key(Guid id) => new() { ["sessionId"] = new AttributeValue(id.ToString()) };
    private static AttributeValue Number(long value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };
}
