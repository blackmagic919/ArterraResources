using Arterra.Game.Schemas;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Arterra.Game.Storage;

public sealed class DynamoConnectionTicketStore(IAmazonDynamoDB database, string table, TimeProvider clock) : IConnectionTicketStore {
    public async Task SaveAsync(string ticket, ConnectionTicket value, CancellationToken cancellationToken) {
        await database.PutItemAsync(new PutItemRequest {
            TableName = table,
            Item = new() {
                ["ticketHash"] = new AttributeValue(Hash(ticket)),
                ["userId"] = new AttributeValue(value.UserId.ToString()),
                ["sessionId"] = new AttributeValue(value.SessionId.ToString()),
                ["role"] = new AttributeValue(value.Role.ToString()),
                ["expiresAt"] = Number(value.ExpiresAt.ToUnixTimeSeconds()),
            },
            ConditionExpression = "attribute_not_exists(ticketHash)",
        }, cancellationToken);
    }

    public async Task<ConnectionTicket?> ConsumeAsync(string ticket, CancellationToken cancellationToken) {
        if (ticket == null || ticket.Length != 64 || !ticket.All(Uri.IsHexDigit)) return null;
        try {
            // Delete + ALL_OLD is the atomic claim. TTL cleanup alone must never authorize a ticket.
            var result = await database.DeleteItemAsync(new DeleteItemRequest {
                TableName = table,
                Key = new() { ["ticketHash"] = new AttributeValue(Hash(ticket)) },
                ConditionExpression = "expiresAt > :now",
                ExpressionAttributeValues = new() { [":now"] = Number(clock.GetUtcNow().ToUnixTimeSeconds()) },
                ReturnValues = ReturnValue.ALL_OLD,
            }, cancellationToken);
            var item = result.Attributes;
            return new ConnectionTicket(Guid.Parse(item["userId"].S), Guid.Parse(item["sessionId"].S),
                Enum.Parse<ConnectionRole>(item["role"].S),
                DateTimeOffset.FromUnixTimeSeconds(long.Parse(item["expiresAt"].N, CultureInfo.InvariantCulture)));
        } catch (ConditionalCheckFailedException) { return null; }
    }

    private static string Hash(string ticket) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ticket)));
    private static AttributeValue Number(long value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };
}
