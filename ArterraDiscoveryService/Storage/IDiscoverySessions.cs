using System.Net;

namespace Arterra.Discovery.Storage;

public interface IDiscoverySessions {
    Task<bool> ActivateAsync(Guid sessionId, Guid ownerId, Guid leaseId, IPEndPoint endpoint, CancellationToken cancel);
    Task<bool> RenewAsync(Guid sessionId, Guid ownerId, Guid leaseId, CancellationToken cancel);
    Task ReleaseAsync(Guid sessionId, Guid ownerId, Guid leaseId, CancellationToken cancel);
    Task<bool> TryReserveConnectionAsync(Guid sessionId, Guid leaseId, Guid introductionId, CancellationToken cancel);
    Task ReleaseConnectionAsync(Guid sessionId, Guid leaseId, Guid introductionId, CancellationToken cancel);
}
