using Arterra.Game.Schemas;
using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Arterra.Game.Storage;

public sealed class DynamoLoginTokenStore(IAmazonDynamoDB database, string table) : ILoginTokenStore {
    public async Task SaveAsync(string tokenHash, AuthenticatedUser user, DateTimeOffset expiresAt, CancellationToken cancellationToken) {
        var request = new PutItemRequest {
            TableName = table,
            Item = new() {
                ["tokenHash"] = new AttributeValue(tokenHash),
                ["userId"] = new AttributeValue(user.UserId.ToString()),
                ["expiresAt"] = new AttributeValue { N = expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) },
            },
            ConditionExpression = "attribute_not_exists(tokenHash)",
        };
        if (user.DisplayName != null) request.Item["displayName"] = new AttributeValue(user.DisplayName);
        await database.PutItemAsync(request, cancellationToken);
    }

    public async Task<AuthenticatedUser?> FindUserAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken) {
        var result = await database.GetItemAsync(new GetItemRequest {
            TableName = table,
            Key = new() { ["tokenHash"] = new AttributeValue(tokenHash) },
            ConsistentRead = true,
        }, cancellationToken);

        var item = result.Item;
        if (item == null || item.Count == 0 || long.Parse(item["expiresAt"].N, CultureInfo.InvariantCulture) <= now.ToUnixTimeSeconds())
            return null;

        return new AuthenticatedUser(Guid.Parse(item["userId"].S), item.TryGetValue("displayName", out var name) ? name.S : null);
    }
}
