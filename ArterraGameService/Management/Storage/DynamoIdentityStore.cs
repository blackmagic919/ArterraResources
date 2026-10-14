using Arterra.Game.Schemas;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Arterra.Game.Management.Auth;

namespace Arterra.Game.Management.Storage;

public sealed class DynamoIdentityStore(IAmazonDynamoDB database, string table) : IIdentityStore {
    public async Task<Guid> GetOrCreateUserAsync(ExternalIdentity identity, CancellationToken cancellationToken) {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { identity.Issuer, identity.Subject }))));

        // One atomic update makes simultaneous first logins resolve to the same user.
        var result = await database.UpdateItemAsync(new UpdateItemRequest {
            TableName = table,
            Key = new() { ["identityKey"] = new AttributeValue(key) },
            UpdateExpression = "SET userId = if_not_exists(userId, :user), issuer = :issuer, subject = :subject",
            ExpressionAttributeValues = new() {
                [":user"] = new AttributeValue(Guid.NewGuid().ToString()),
                [":issuer"] = new AttributeValue(identity.Issuer),
                [":subject"] = new AttributeValue(identity.Subject),
            },
            ReturnValues = ReturnValue.ALL_NEW,
        }, cancellationToken);

        return Guid.Parse(result.Attributes["userId"].S);
    }
}
