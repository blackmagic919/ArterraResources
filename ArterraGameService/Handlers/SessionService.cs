using Arterra.Game.Schemas;
using FluentValidation;
using Arterra.Game.Storage;
using System.Security.Cryptography;

namespace Arterra.Game.Handlers;

public sealed class SessionService(ISessionStore sessions, ISessionMetadata metadata,
    NetworkEndpoint connectivityEndpoint, IConnectionTicketStore tickets, TimeProvider clock,
    IValidator<CreateSessionRequest> createValidator, IValidator<SessionQuery> queryValidator) {

    public async Task<CreateSessionResponse> CreateAsync(Guid userId, CreateSessionRequest request, CancellationToken cancel) {
        await createValidator.ValidateAndThrowAsync(request, cancel);

        var now = clock.GetUtcNow();
        var session = new SessionRecord(Guid.NewGuid(), userId, request.Name.Trim(), "uploading", now.AddHours(1), request.MaxConnections, 0, null);
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

    public async Task<SessionList> ListAsync(int limit, string? cursor, CancellationToken cancel) {
        await queryValidator.ValidateAndThrowAsync(new SessionQuery(limit, cursor), cancel);
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

    public async Task<ConnectionTicketResponse> HostAsync(Guid id, Guid userId, CancellationToken cancel) {
        var session = await OwnedAsync(id, userId, cancel);
        if (session.Status != "ready")
            throw new SessionRequestException(409, "Complete the metadata upload and disconnect any existing host before requesting a hosting ticket.");
        return await IssueConnectionTicketAsync(userId, id, ConnectionRole.Host, cancel);
    }

    public async Task<JoinSessionResponse> JoinAsync(Guid id, Guid userId, CancellationToken cancel) {
        var session = await GetActiveAsync(id, cancel);
        var connection = await IssueConnectionTicketAsync(userId, id, ConnectionRole.Join, cancel);
        return new JoinSessionResponse(session, connection);
    }

    public async Task DeleteAsync(Guid id, Guid userId, CancellationToken cancel) {
        await OwnedAsync(id, userId, cancel);
        if (!await sessions.DeleteAsync(id, userId, cancel))
            throw new SessionRequestException(409, "Session is currently being completed. Retry deletion shortly.");
    }

    private async Task<ConnectionTicketResponse> IssueConnectionTicketAsync(
        Guid userId, Guid sessionId, ConnectionRole role, CancellationToken cancel) {
        var expiresAt = clock.GetUtcNow().AddSeconds(60);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await tickets.SaveAsync(token, new ConnectionTicket(userId, sessionId, role, expiresAt), cancel);
        return new ConnectionTicketResponse(token, expiresAt, connectivityEndpoint);
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
            session.HostEndpoint!, connectivityEndpoint, urls.Metadata, urls.DisplayChunk, session.ConnectionCount, session.MaxConnections);
    }
}

public sealed class SessionRequestException(int statusCode, string message) : Exception(message) {
    public int StatusCode { get; } = statusCode;
}
