using Arterra.Game.Schemas;

namespace Arterra.Game.Storage;

public interface IConnectionTicketStore {
    Task SaveAsync(string ticket, ConnectionTicket value, CancellationToken cancellationToken);
    Task<ConnectionTicket?> ConsumeAsync(string ticket, CancellationToken cancellationToken);
}
