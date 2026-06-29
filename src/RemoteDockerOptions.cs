namespace AditiKraft.Aspire.Hosting.RemoteDocker;

public sealed class RemoteDockerOptions : IDisposable
{
    private readonly List<PortMap> _portMappings = [];
    private SshPortForwarder? _forwarder;

    internal RemoteDockerOptions(bool isEnabled, string? remoteHost, string? sshUser, string? sshKeyFile, string? sshPassword)
    {
        IsEnabled = isEnabled;
        RemoteHost = remoteHost;
        SshUser = sshUser;
        SshKeyFile = sshKeyFile;
        SshPassword = sshPassword;
    }

    public bool IsEnabled { get; }

    public IReadOnlyList<PortMap> PortMappings => _portMappings;
    internal string? RemoteHost { get; }
    internal string? SshUser { get; }
    internal string? SshKeyFile { get; }
    internal string? SshPassword { get; }

    public PortMap AddPortMapping(int publicPort, int privatePort, bool portForward = true)
    {
        PortMap mapping = new(publicPort, privatePort, portForward);
        _portMappings.Add(mapping);
        return mapping;
    }

    /// <summary>
    /// Establishes the SSH connection and starts all configured port forwards.
    /// Call this immediately before <c>builder.Build().Run()</c> so the tunnel is
    /// up before the orchestrator starts resources and runs health checks.
    /// Idempotent: subsequent calls are no-ops while a forwarder is active.
    /// </summary>
    public void Connect()
    {
        if (!IsEnabled || _forwarder is not null)
        {
            return;
        }

        _forwarder = new SshPortForwarder(RemoteHost!, SshUser!, SshKeyFile, SshPassword);
        _forwarder.Connect();

        foreach (PortMap portMap in _portMappings.Where(p => p.PortForward))
        {
            Console.WriteLine($"Forwarding local port {portMap.PublicPort} to remote Docker host port {portMap.PrivatePort}");
            _forwarder.AddForwardedPort(portMap.PublicPort, portMap.PrivatePort);
        }
    }

    public void Dispose()
    {
        _forwarder?.Dispose();
        _forwarder = null;
    }
}
