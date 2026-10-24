namespace Arterra.Game.Schemas;

public sealed record SessionRecord(Guid SessionId, Guid OwnerId, string Name, string Status,
    DateTimeOffset ExpiresAt, int? MaxConnections, int ConnectionCount, NetworkEndpoint? HostEndpoint);
public sealed record SessionPage(IReadOnlyList<SessionRecord> Sessions, string? NextCursor);
public sealed record CreateSessionRequest(string Name, long MetadataBytes, long DisplayChunkBytes, int? MaxConnections = null);
public sealed record UploadTarget(string Url, string ContentType, long ContentLength);
public sealed record CreateSessionResponse(Guid SessionId, DateTimeOffset ExpiresAt, DateTimeOffset UploadsExpireAt,
    UploadTarget Metadata, UploadTarget DisplayChunk);
public sealed record SessionView(Guid SessionId, string Name, DateTimeOffset ExpiresAt,
    NetworkEndpoint HostEndpoint, NetworkEndpoint ConnectivityEndpoint, string MetadataUrl, string DisplayChunkUrl, int ConnectionCount, int? MaxConnections);
public sealed record SessionList(IReadOnlyList<SessionView> Sessions, string? NextCursor);
public sealed record JoinSessionResponse(SessionView Session, ConnectionTicketResponse Connection);

public sealed record SessionQuery(int Limit, string? Cursor);
