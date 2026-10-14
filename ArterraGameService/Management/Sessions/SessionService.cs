using Arterra.Game.Schemas;
namespace Arterra.Game.Management.Sessions;

public sealed class SessionService(ISessionStore sessions, ISessionMetadata metadata,
    NetworkEndpoint connectivityEndpoint, TimeProvider clock) {

    public const long MaximumMetadataBytes = 1024 * 1024;
    public const long MaximumChunkBytes = 16 * 1024 * 1024;

    public async Task<CreateSessionResponse> CreateAsync(Guid userId, CreateSessionRequest request, CancellationToken cancel) {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 128)
            throw new SessionRequestException(400, "name must contain 1–128 characters.");
        if (request.MetadataBytes is < 2 or > MaximumMetadataBytes || request.DisplayChunkBytes is < 1 or > MaximumChunkBytes)
            throw new SessionRequestException(400, "metadataBytes must be 2–1048576 and displayChunkBytes 1–16777216.");

        var now = clock.GetUtcNow();
        var session = new SessionRecord(Guid.NewGuid(), userId, request.Name.Trim(), "uploading", now.AddHours(1), null);
        var uploadExpiry = now.AddMinutes(10);
        var uploads = await metadata.CreateUploadsAsync(session.SessionId, request.MetadataBytes, request.DisplayChunkBytes, uploadExpiry);
        await sessions.CreateAsync(session, cancel);
        return new CreateSessionResponse(session.SessionId, session.ExpiresAt, uploadExpiry, uploads.Metadata, uploads.DisplayChunk);
    }

    public async Task CompleteAsync(Guid id, Guid userId, CancellationToken cancel) {
        var session = await OwnedAsync(id, userId, cancel);
        if (session.Status is "ready" or "active") return;
        if (!await sessions.BeginPublicationAsync(id, userId, cancel))
            throw new SessionRequestException(409, "Session cannot be completed in its current state.");

        try {
            await metadata.PublishAsync(id, cancel);
        } catch {
            // A failed copy remains undiscoverable; a subsequent completion can retry the upload.
            await sessions.FinishPublicationAsync(id, false, cancel);
            throw;
        }
        await sessions.FinishPublicationAsync(id, true, cancel);
    }

    public async Task<SessionList> ListAsync(int limit, Guid? cursor, CancellationToken cancel) {
        if (limit is < 1 or > 100) throw new SessionRequestException(400, "limit must be between 1 and 100.");
        var page = await sessions.ListAsync(limit, cursor, cancel);
        var views = new List<SessionView>();
        foreach (var session in page.Sessions) {
            if (IsActive(session)) views.Add(await ViewAsync(session));
        }
        return new SessionList(views, page.NextCursor);
    }

    public async Task<SessionView> GetActiveAsync(Guid id, CancellationToken cancel) {
        var session = await sessions.GetAsync(id, cancel);
        if (session == null || !IsActive(session)) throw new SessionRequestException(404, "Active session not found.");
        return await ViewAsync(session);
    }

    public async Task DeleteAsync(Guid id, Guid userId, CancellationToken cancel) {
        await OwnedAsync(id, userId, cancel);
        if (!await sessions.DeleteAsync(id, userId, cancel))
            throw new SessionRequestException(409, "Session is currently being completed. Retry deletion shortly.");
    }

    private async Task<SessionRecord> OwnedAsync(Guid id, Guid ownerId, CancellationToken cancel) {
        var session = await sessions.GetAsync(id, cancel);
        if (session == null || session.OwnerId != ownerId || session.ExpiresAt <= clock.GetUtcNow())
            throw new SessionRequestException(404, "Session not found.");
        return session;
    }

    private bool IsActive(SessionRecord session) => session.Status == "active"
        && session.ExpiresAt > clock.GetUtcNow() && session.HostEndpoint != null;

    private async Task<SessionView> ViewAsync(SessionRecord session) {
        var expiry = clock.GetUtcNow().AddMinutes(5);
        if (session.ExpiresAt < expiry) expiry = session.ExpiresAt;
        var urls = await metadata.GetDownloadsAsync(session.SessionId, expiry);
        return new SessionView(session.SessionId, session.Name, session.ExpiresAt,
            session.HostEndpoint!, connectivityEndpoint, urls.Metadata, urls.DisplayChunk);
    }
}
