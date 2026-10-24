using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arterra.Game.Schemas;

// Exactly one provider property is accepted, e.g. { "unity": { "authenticationToken": "..." } }.
public sealed record LoginRequest(JsonElement Credential);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UnityCredential(string AuthenticationToken);

public sealed record ExternalIdentity(string Issuer, string Subject, DateTimeOffset ExpiresAt, string? DisplayName = null);
public sealed record AuthenticatedUser(Guid UserId, string? DisplayName = null);
public sealed record UnityPlayerName(string? Name);
public sealed record LoginResponse(Guid UserId, string SessionToken, DateTimeOffset ExpiresAt);
