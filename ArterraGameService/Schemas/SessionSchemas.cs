namespace Arterra.Game.Schemas;

public sealed record SessionRecord(Guid SessionId, Guid OwnerId, string Name, string Status,
    DateTimeOffset ExpiresAt, int? MaxConnections, int ConnectionCount, NetworkEndpoint? HostEndpoint, string? HostDisplayName = null);
public sealed record SessionPage(IReadOnlyList<SessionRecord> Sessions, string? NextCursor);
public sealed record CreateSessionRequest(string Name, long PreviewInfoBytes, long DetailedInfoBytes, int? MaxConnections = null);
public sealed record UploadTarget(string Url, string ContentType, long ContentLength);
public sealed record CreateSessionResponse(Guid SessionId, DateTimeOffset ExpiresAt, DateTimeOffset UploadsExpireAt,
    UploadTarget PreviewInfo, UploadTarget DetailedInfo);
public sealed record SessionView(Guid SessionId, string Name, DateTimeOffset ExpiresAt,
    NetworkEndpoint HostEndpoint, string PreviewInfoUrl, string DetailedInfoUrl, int ConnectionCount, int? MaxConnections, string? HostDisplayName = null);
public sealed record SessionList(IReadOnlyList<SessionView> Sessions, string? NextCursor);
public sealed record JoinSessionResponse(SessionView Session, ConnectionTicketResponse Connection);

public sealed record SessionQuery(int Limit, string? Cursor);
