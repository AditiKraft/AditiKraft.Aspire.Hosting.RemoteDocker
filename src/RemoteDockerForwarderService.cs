using Microsoft.Extensions.Hosting;

namespace AditiKraft.Aspire.Hosting.RemoteDocker;

/// <summary>
/// Owns the lifetime of the SSH tunnel so callers don't have to. The DI container
/// creates and disposes this service with the host, so the forwarder is torn down
/// gracefully on shutdown without any try/finally in the AppHost.
/// Connecting still happens via <see cref="RemoteDockerOptions.Connect"/> (driven by
/// the <c>BeforeStartEvent</c> subscription), which guarantees the tunnel is up before
/// the orchestrator starts resources. <see cref="RemoteDockerOptions.Connect"/> is
/// idempotent, so an explicit pre-Build call by the caller is also safe.
/// </summary>
internal sealed class RemoteDockerForwarderService(RemoteDockerOptions options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        options.Dispose();
        return Task.CompletedTask;
    }
}
