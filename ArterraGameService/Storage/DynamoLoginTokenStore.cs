using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Arterra.Game.Storage;

public sealed class DynamoLoginTokenStore(IAmazonDynamoDB database, string table) : ILoginTokenStore {
    public async Task SaveAsync(string tokenHash, Guid userId, DateTimeOffset expiresAt, CancellationToken cancellationToken) {
        await database.PutItemAsync(new PutItemRequest {
            TableName = table,
            Item = new() {
                ["tokenHash"] = new AttributeValue(tokenHash),
                ["userId"] = new AttributeValue(userId.ToString()),
                ["expiresAt"] = new AttributeValue { N = expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) },
            },
            ConditionExpression = "attribute_not_exists(tokenHash)",
        }, cancellationToken);
    }

    public async Task<Guid?> FindUserAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken) {
        var result = await database.GetItemAsync(new GetItemRequest {
            TableName = table,
            Key = new() { ["tokenHash"] = new AttributeValue(tokenHash) },
            ConsistentRead = true,
        }, cancellationToken);

        var item = result.Item;
        if (item == null || item.Count == 0 || long.Parse(item["expiresAt"].N, CultureInfo.InvariantCulture) <= now.ToUnixTimeSeconds())
            return null;

        return Guid.Parse(item["userId"].S);
    }
}
