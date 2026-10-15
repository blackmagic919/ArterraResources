using Arterra.Game.Storage;
using System.Net;
using System.Runtime.InteropServices;
using Amazon.DynamoDBv2;
using Arterra.Discovery.Storage;
using Arterra.Discovery.Transport;

int port = int.Parse(Environment.GetEnvironmentVariable("ARTERRA_UDP_PORT") ?? "9050");
if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) => {
    args.Cancel = true;
    shutdown.Cancel();
};
using var termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => {
    context.Cancel = true;
    shutdown.Cancel();
});
using var database = new AmazonDynamoDBClient();
var clock = TimeProvider.System;
var tickets = new DynamoConnectionTicketStore(database, Required("TICKET_TABLE"), clock);
var sessions = new DynamoDiscoverySessions(database, Required("SESSION_TABLE"), clock);
var server = new DiscoveryServer(tickets, sessions, clock);

try {
    server.Start(IPAddress.Any, port);
    while (!shutdown.IsCancellationRequested) {
        server.Tick();
        await Task.Delay(15, shutdown.Token);
    }
} catch (OperationCanceledException) when (shutdown.IsCancellationRequested) {
    // Normal systemd shutdown.
} finally {
    await server.StopAsync();
}

static string Required(string name) => Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"{name} is required.");
