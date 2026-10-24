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
        values[":one"] = Number(1);
        values[":ip"] = new AttributeValue(endpoint.Address.ToString());
        values[":port"] = Number(endpoint.Port);
        values[":expiry"] = Number((clock.GetUtcNow() + HostLease).ToUnixTimeSeconds());
        return UpdateAsync(sessionId,
            "SET #status = :active, hostLeaseId = :lease, expiresAt = :expiry, hostIp = :ip, hostPort = :port, connectionCount = :one REMOVE connectionIds",
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
        values[":zero"] = Number(0);
        // Preserve the existing expiry so an owner may reconnect within the remaining lease.
        await UpdateAsync(sessionId, "SET #status = :ready, connectionCount = :zero REMOVE hostLeaseId, hostIp, hostPort, connectionIds",
            "ownerId = :owner AND hostLeaseId = :lease AND #status = :active", values, cancel);
    }

    public Task<bool> TryReserveConnectionAsync(Guid sessionId, Guid leaseId, Guid introductionId, CancellationToken cancel) {
        var values = ConnectionValues(leaseId, introductionId);
        values[":now"] = Number(clock.GetUtcNow().ToUnixTimeSeconds());
        values[":one"] = Number(1);
        // Count and reservation identity change together; retries cannot allocate twice.
        return UpdateAsync(sessionId,
            "SET connectionCount = connectionCount + :one ADD connectionIds :ids",
            "#status = :active AND hostLeaseId = :lease AND expiresAt > :now " +
            "AND (attribute_not_exists(maxConnections) OR connectionCount < maxConnections) " +
            "AND (attribute_not_exists(connectionIds) OR NOT contains(connectionIds, :id))", values, cancel);
    }

    public async Task ReleaseConnectionAsync(Guid sessionId, Guid leaseId, Guid introductionId, CancellationToken cancel) {
        var values = ConnectionValues(leaseId, introductionId);
        values[":one"] = Number(1);
        // A duplicate departure or an old host lease cannot decrement the current count.
        await UpdateAsync(sessionId,
            "SET connectionCount = connectionCount - :one DELETE connectionIds :ids",
            "#status = :active AND hostLeaseId = :lease AND contains(connectionIds, :id) AND connectionCount > :one",
            values, cancel);
    }

    private static Dictionary<string, AttributeValue> ConnectionValues(Guid leaseId, Guid introductionId) => new() {
        [":lease"] = new AttributeValue(leaseId.ToString()),
        [":active"] = new AttributeValue("active"),
        [":id"] = new AttributeValue(introductionId.ToString()),
        [":ids"] = new AttributeValue { SS = [introductionId.ToString()] },
    };

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
