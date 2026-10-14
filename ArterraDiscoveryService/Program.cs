using System.Net;
using System.Runtime.InteropServices;
using LiteNetLib;

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

var listener = new EventBasedNetListener();
// This is only a process/deployment scaffold. Session and introduction protocols are deferred.
listener.ConnectionRequestEvent += request => request.Reject();
var network = new NetManager(listener) { NatPunchEnabled = false, IPv6Enabled = false };

try {
    if (!network.Start(IPAddress.Any, IPAddress.IPv6Any, port))
        throw new IOException($"Could not bind UDP port {port}.");

    Console.WriteLine($"{DateTimeOffset.UtcNow:O} LiteNetLib {typeof(NetManager).Assembly.GetName().Version}: " +
        $"placeholder listening on UDP {port}. Introduction handling is not implemented.");

    while (!shutdown.IsCancellationRequested) {
        network.PollEvents();
        await Task.Delay(15, shutdown.Token);
    }
} catch (OperationCanceledException) when (shutdown.IsCancellationRequested) {
    // Normal systemd shutdown.
} finally {
    network.Stop();
    Console.WriteLine($"{DateTimeOffset.UtcNow:O} Discovery placeholder stopped.");
}
