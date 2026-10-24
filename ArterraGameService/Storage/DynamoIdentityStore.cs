using Arterra.Game.Schemas;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Arterra.Game.Storage;

public sealed class DynamoIdentityStore(IAmazonDynamoDB database, string table) : IIdentityStore {
    public async Task<Guid> GetOrCreateUserAsync(ExternalIdentity identity, CancellationToken cancellationToken) {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { identity.Issuer, identity.Subject }))));

        // One atomic update makes simultaneous first logins resolve to the same user.
        var request = new UpdateItemRequest {
            TableName = table,
            Key = new() { ["identityKey"] = new AttributeValue(key) },
            UpdateExpression = "SET userId = if_not_exists(userId, :user), issuer = :issuer, subject = :subject",
            ExpressionAttributeValues = new() {
                [":user"] = new AttributeValue(Guid.NewGuid().ToString()),
                [":issuer"] = new AttributeValue(identity.Issuer),
                [":subject"] = new AttributeValue(identity.Subject),
            },
            ReturnValues = ReturnValue.ALL_NEW,
        };
        if (identity.DisplayName != null) {
            request.UpdateExpression += ", displayName = :name";
            request.ExpressionAttributeValues[":name"] = new AttributeValue(identity.DisplayName);
        } else request.UpdateExpression += " REMOVE displayName";
        var result = await database.UpdateItemAsync(request, cancellationToken);

        return Guid.Parse(result.Attributes["userId"].S);
    }
}
