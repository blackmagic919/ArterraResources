using Arterra.Game.Schemas;
using FluentValidation;
using Arterra.Game.Storage;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Arterra.Discovery.Storage;
using LiteNetLib;

namespace Arterra.Discovery.Transport;

public sealed class DiscoveryServer {
    private sealed class Client(NetPeer peer, DateTimeOffset deadline) {
        public readonly NetPeer Peer = peer;
        public readonly Guid LeaseId = Guid.NewGuid();
        public DateTimeOffset? Deadline = deadline;
        public ConnectionTicket? Ticket;
        public IPEndPoint? LocalEndpoint;
        public bool Busy;
        public DateTimeOffset RenewAt;
        public DateTimeOffset LeaseExpires;
        public Introduction? Introduction;
        public readonly HashSet<Guid> Departures = new();
    }

    private sealed class Introduction(Client host, Guid id, DateTimeOffset now) {
        public readonly Client Host = host;
        public readonly Guid Id = id;
        public readonly DateTimeOffset ExpiresAt = now.AddSeconds(10);
        public DateTimeOffset SendAt = now;
    }

    private readonly NetManager network;
    private readonly IConnectionTicketStore tickets;
    private readonly IDiscoverySessions sessions;
    private readonly TimeProvider clock;
    private readonly Dictionary<NetPeer, Client> clients = new();
    private readonly Dictionary<Guid, Client> hosts = new();
    private readonly ConcurrentQueue<Action> completions = new();
    private readonly List<Task> operations = new();
    private bool stopping;

    public int LocalPort => network.LocalPort;

    public DiscoveryServer(IConnectionTicketStore tickets, IDiscoverySessions sessions, TimeProvider clock) {
        this.tickets = tickets;
        this.sessions = sessions;
        this.clock = clock;
        var listener = new EventBasedNetListener();
        network = new NetManager(listener) {
            IPv6Enabled = false, AutoRecycle = true, DisconnectTimeout = 10000,
            // Introductions are sent after authentication over connected messages.
            // The mediator never processes untrusted NAT requests/responses itself.
            NatPunchEnabled = false,
        };
        listener.ConnectionRequestEvent += OnConnectionRequest;
        listener.NetworkReceiveEvent += (peer, reader, _, _) => {
            if (!clients.TryGetValue(peer, out var client)) return;
            if (reader.AvailableBytes > DiscoveryProtocol.MaximumMessageBytes) {
                Close(client, "invalidMessage");
                return;
            }
            try { Receive(client, DiscoveryProtocol.Decode(reader.GetRemainingBytes())); }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or ValidationException) {
                Close(client, "invalidMessage");
            }
        };
        listener.PeerDisconnectedEvent += (peer, _) => {
            if (clients.TryGetValue(peer, out var client)) Remove(client);
        };
        listener.NetworkErrorEvent += (_, error) => Log($"UDP error: {error}");
    }

    public void Start(IPAddress address, int port) {
        if (!network.Start(address, IPAddress.IPv6Any, port)) throw new IOException($"Could not bind UDP port {port}.");
        Log($"Listening on UDP {network.LocalPort}; protocol {DiscoveryProtocol.Version}, direct connections only.");
    }

    public void Tick() {
        while (completions.TryDequeue(out var complete)) complete();
        operations.RemoveAll(task => task.IsCompleted);
        network.PollEvents();
        var now = clock.GetUtcNow();

        foreach (var client in clients.Values.ToArray()) {
            if (!IsConnected(client)) continue;
            if (client.Deadline <= now) { Close(client, "timeout"); continue; }
            if (client.Ticket?.Role == ConnectionRole.Host && hosts.GetValueOrDefault(client.Ticket.SessionId) == client) {
                if (client.LeaseExpires <= now) { Close(client, "hostLeaseExpired"); continue; }
                if (!client.Busy && client.RenewAt <= now) Renew(client);
            }

            if (client.Introduction is not { } introduction) continue;
            if (!IsConnected(introduction.Host)) { Close(client, "hostUnavailable"); continue; }
            if (introduction.ExpiresAt <= now) { Finish(client, false); continue; }
            if (introduction.SendAt > now) continue;

            network.NatPunchModule.NatIntroduce(introduction.Host.LocalEndpoint!, introduction.Host.Peer,
                client.LocalEndpoint!, client.Peer, introduction.Id.ToString("N"));
            introduction.SendAt = now.AddSeconds(1);
        }
    }

    private void OnConnectionRequest(ConnectionRequest request) {
        if (stopping || request.Data.AvailableBytes != sizeof(int) || request.Data.GetInt() != DiscoveryProtocol.Version) {
            request.RejectForce(DiscoveryProtocol.Encode(new DiscoveryError("protocolMismatch")));
            return;
        }
        var peer = request.Accept();
        if (peer != null) clients[peer] = new Client(peer, clock.GetUtcNow().AddSeconds(5));
    }

    private void Receive(Client client, DiscoveryClientMessage? message) {
        switch (message) {
            case AuthenticateConnection auth when client.Ticket == null && !client.Busy:
                client.LocalEndpoint = new IPEndPoint(IPAddress.Parse(auth.LocalEndpoint.Host), auth.LocalEndpoint.Port);
                client.Busy = true;
                client.Deadline = clock.GetUtcNow().AddSeconds(15);
                Schedule(async cancel => {
                    var ticket = await tickets.ConsumeAsync(auth.Ticket, cancel);
                    return () => {
                        if (!IsConnected(client)) return;
                        client.Busy = false;
                        if (ticket == null) { Close(client, "invalidTicket"); return; }
                        client.Ticket = ticket;
                        if (ticket.Role == ConnectionRole.Host) Activate(client);
                        else BeginIntroduction(client);
                    };
                }, exception => Fail(client, exception));
                break;
            case ConnectionDeparted departure when client.Ticket?.Role == ConnectionRole.Host
                && hosts.GetValueOrDefault(client.Ticket.SessionId) == client:
                client.Departures.Add(departure.IntroductionId);
                client.RenewAt = clock.GetUtcNow();
                break;
            case IntroductionResult result when client.Introduction?.Id == result.IntroductionId:
                Finish(client, result.Connected);
                break;
            default:
                Close(client, "invalidMessage");
                break;
        }
    }

    private void Activate(Client client) {
        var ticket = client.Ticket!;
        client.Busy = true;
        var started = clock.GetUtcNow();
        Schedule(async cancel => {
            var active = await sessions.ActivateAsync(ticket.SessionId, ticket.UserId, client.LeaseId, client.Peer, cancel);
            return () => {
                client.Busy = false;
                if (!IsConnected(client)) {
                    if (active) Release(client);
                    return;
                }
                if (!active) { Close(client, "sessionNotReady"); return; }
                hosts[ticket.SessionId] = client;
                client.Deadline = null;
                client.LeaseExpires = started.AddSeconds(90);
                client.RenewAt = started.AddSeconds(30);
                Send(client, new HostReady(ticket.SessionId));
                Log($"Host active: session {ticket.SessionId}, user {ticket.UserId}.");
            };
        }, exception => Fail(client, exception));
    }

    private void Renew(Client client) {
        var ticket = client.Ticket!;
        client.Busy = true;
        var started = clock.GetUtcNow();
        var departures = client.Departures.ToArray();
        Schedule(async cancel => {
            foreach (var id in departures)
                await sessions.ReleaseConnectionAsync(ticket.SessionId, client.LeaseId, id, cancel);
            var renewed = await sessions.RenewAsync(ticket.SessionId, ticket.UserId, client.LeaseId, cancel);
            return () => {
                client.Busy = false;
                if (!IsConnected(client)) return;
                if (!renewed) { Close(client, "sessionUnavailable"); return; }
                foreach (var id in departures) client.Departures.Remove(id);
                client.LeaseExpires = started.AddSeconds(90);
                client.RenewAt = started.AddSeconds(30);
            };
        }, exception => {
            client.Busy = false;
            client.RenewAt = clock.GetUtcNow().AddSeconds(5);
            Log($"Host renewal failed for {ticket.SessionId}: {exception.GetType().Name}: {exception.Message}; retrying within lease.");
        });
    }

    private void BeginIntroduction(Client client) {
        var ticket = client.Ticket!;
        if (!hosts.TryGetValue(ticket.SessionId, out var host) || !IsConnected(host)) {
            Close(client, "hostUnavailable");
            return;
        }
        client.Busy = true;
        var id = Guid.NewGuid();
        Schedule(async cancel => {
            var active = await sessions.TryReserveConnectionAsync(ticket.SessionId, host.LeaseId, id, cancel);
            return () => {
                client.Busy = false;
                if (!active) { if (IsConnected(client)) Close(client, "sessionFullOrUnavailable"); return; }
                if (!IsConnected(client) || !IsConnected(host)) {
                    QueueDeparture(host, id);
                    if (IsConnected(client)) Close(client, "hostUnavailable");
                    return;
                }
                var introduction = new Introduction(host, id, clock.GetUtcNow());
                client.Introduction = introduction;
                client.Deadline = null;
                Send(host, new PeerIntroduction(ticket.SessionId, introduction.Id, ticket.UserId));
                Send(client, new PeerIntroduction(ticket.SessionId, introduction.Id, host.Ticket!.UserId));
                Log($"Introducing user {ticket.UserId} to session {ticket.SessionId}, attempt {introduction.Id}.");
            };
        }, exception => {
            QueueDeparture(host, id);
            Fail(client, exception);
        });
    }

    private void QueueDeparture(Client host, Guid id) {
        host.Departures.Add(id);
        host.RenewAt = clock.GetUtcNow();
    }

    private void Finish(Client client, bool connected) {
        var introduction = client.Introduction!;
        client.Introduction = null;
        if (!connected) QueueDeparture(introduction.Host, introduction.Id);
        if (IsConnected(introduction.Host)) Send(introduction.Host, new IntroductionFinished(introduction.Id, connected));
        client.Peer.Disconnect(DiscoveryProtocol.Encode(new IntroductionFinished(introduction.Id, connected)));
        Remove(client);
        Log($"Introduction {introduction.Id} finished: connected={connected}.");
    }

    private void Close(Client client, string code) {
        client.Peer.Disconnect(DiscoveryProtocol.Encode(new DiscoveryError(code)));
        Remove(client);
        Log($"Discovery connection closed: {code}.");
    }

    private void Remove(Client client) {
        if (!clients.Remove(client.Peer)) return;
        if (client.Introduction is { } introduction) {
            QueueDeparture(introduction.Host, introduction.Id);
            if (IsConnected(introduction.Host))
                Send(introduction.Host, new IntroductionFinished(introduction.Id, false));
        }
        client.Introduction = null;

        if (client.Ticket?.Role != ConnectionRole.Host) return;
        if (hosts.GetValueOrDefault(client.Ticket.SessionId) == client) hosts.Remove(client.Ticket.SessionId);
        Release(client);
        foreach (var waiting in clients.Values.Where(other => other.Introduction?.Host == client).ToArray())
            Close(waiting, "hostUnavailable");
        Log($"Host disconnected: session {client.Ticket.SessionId}.");
    }

    private void Release(Client client) {
        var ticket = client.Ticket!;
        Schedule(async cancel => {
            await sessions.ReleaseAsync(ticket.SessionId, ticket.UserId, client.LeaseId, cancel);
            return () => { };
        }, exception => Log($"Session release failed for {ticket.SessionId}: {exception.GetType().Name}: {exception.Message}; expiry remains the fallback."));
    }

    private bool IsConnected(Client client) => !stopping && clients.ContainsKey(client.Peer)
        && client.Peer.ConnectionState == ConnectionState.Connected;

    private static void Send(Client client, DiscoveryServerMessage message) =>
        client.Peer.Send(DiscoveryProtocol.Encode(message), DeliveryMethod.ReliableOrdered);

    private void Fail(Client client, Exception exception) {
        Log($"Discovery storage operation failed: {exception.GetType().Name}: {exception.Message}.");
        if (IsConnected(client)) Close(client, "serviceUnavailable");
    }

    // AWS continuations only enqueue work; peer/session dictionaries belong to the polling loop.
    private void Schedule(Func<CancellationToken, Task<Action>> operation, Action<Exception> failure) {
        operations.Add(RunAsync());
        async Task RunAsync() {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { completions.Enqueue(await operation(timeout.Token)); }
            catch (Exception exception) { completions.Enqueue(() => failure(exception)); }
        }
    }

    public async Task StopAsync() {
        stopping = true;
        Log("Stopping discovery and releasing host sessions.");
        foreach (var client in clients.Values.ToArray()) Remove(client);
        // In-flight activation callbacks may schedule a compensating release after disconnection.
        while (operations.Count > 0 || !completions.IsEmpty) {
            await Task.WhenAll(operations.ToArray());
            operations.RemoveAll(task => task.IsCompleted);
            while (completions.TryDequeue(out var complete)) complete();
        }
        network.Stop();
        Log("Discovery stopped.");
    }

    private static void Log(string message) => Console.WriteLine($"{DateTimeOffset.UtcNow:O} [Discovery] {message}");
}
