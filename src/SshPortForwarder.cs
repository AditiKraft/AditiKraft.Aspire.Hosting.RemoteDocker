using Renci.SshNet;

namespace AditiKraft.Aspire.Hosting.RemoteDocker;

internal sealed class SshPortForwarder : IDisposable
{
    private readonly List<ForwardedPortLocal> _forwardedPorts = [];
    private readonly SshClient _client;

    public SshPortForwarder(string host, string username, string? sshKeyPath, string? password)
    {
        List<AuthenticationMethod> authenticationMethods = [];

        if (!string.IsNullOrWhiteSpace(sshKeyPath))
        {
            PrivateKeyFile privateKey = new(sshKeyPath);
            authenticationMethods.Add(new PrivateKeyAuthenticationMethod(username, privateKey));
        }

        if (!string.IsNullOrWhiteSpace(password))
        {
            authenticationMethods.Add(new PasswordAuthenticationMethod(username, password));
        }

        if (authenticationMethods.Count == 0)
        {
            throw new InvalidOperationException(
                "Either ssh_key_file or ssh_password must be configured for SSH port forwarding.");
        }

        ConnectionInfo connectionInfo = new(host, username, [.. authenticationMethods]);
        _client = new SshClient(connectionInfo) { KeepAliveInterval = TimeSpan.FromSeconds(30) };
    }

    public void Connect() => _client.Connect();

    public void AddForwardedPort(int localPort, int remotePort)
    {
        ForwardedPortLocal port = new("127.0.0.1", (uint)localPort, "127.0.0.1", (uint)remotePort);
        _client.AddForwardedPort(port);
        _forwardedPorts.Add(port);
        port.Exception += (_, e) =>
            Console.WriteLine($"Port forwarding error for {localPort}->{remotePort}: {e.Exception}");
        port.Start();
    }

    public void EnsureDirectoryExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Remote directory path must be configured.");
        }

        SshCommand command =
            _client.RunCommand($"mkdir -p -- {QuoteForShell(path)} && chmod 0777 -- {QuoteForShell(path)}");
        if (command.ExitStatus != 0)
        {
            throw new InvalidOperationException(
                $"Failed to create remote directory '{path}'. Exit status: {command.ExitStatus}. Error: {command.Error}");
        }
    }

    public void Dispose()
    {
        foreach (ForwardedPortLocal forwardedPort in _forwardedPorts)
        {
            forwardedPort.Stop();
        }

        if (_client.IsConnected)
        {
            _client.Disconnect();
        }

        _client.Dispose();
    }

    private static string QuoteForShell(string value) => $"'{value.Replace("'", "'\"'\"'")}'";
}
