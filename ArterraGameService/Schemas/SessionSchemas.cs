namespace Arterra.Game.Schemas;

public sealed record NetworkEndpoint(string Host, int Port);
public sealed record SessionRecord(Guid SessionId, Guid OwnerId, string Name, string Status,
    DateTimeOffset ExpiresAt, NetworkEndpoint? HostEndpoint);
public sealed record SessionPage(IReadOnlyList<SessionRecord> Sessions, Guid? NextCursor);
public sealed record CreateSessionRequest(string Name, long MetadataBytes, long DisplayChunkBytes);
public sealed record UploadTarget(string Url, string ContentType, long ContentLength);
public sealed record CreateSessionResponse(Guid SessionId, DateTimeOffset ExpiresAt, DateTimeOffset UploadsExpireAt,
    UploadTarget Metadata, UploadTarget DisplayChunk);
public sealed record SessionView(Guid SessionId, string Name, DateTimeOffset ExpiresAt,
    NetworkEndpoint HostEndpoint, NetworkEndpoint ConnectivityEndpoint, string MetadataUrl, string DisplayChunkUrl);
public sealed record SessionList(IReadOnlyList<SessionView> Sessions, Guid? NextCursor);
