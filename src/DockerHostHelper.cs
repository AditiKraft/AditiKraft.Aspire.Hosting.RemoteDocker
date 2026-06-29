namespace AditiKraft.Aspire.Hosting.RemoteDocker;

internal static class DockerHostHelper
{
    public static string GetRemoteDockerHostName(string? dockerHostValue)
    {
        if (string.IsNullOrWhiteSpace(dockerHostValue))
        {
            return string.Empty;
        }

        if (dockerHostValue.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase)
            || dockerHostValue.StartsWith("unix://", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string host = dockerHostValue;
        if (Uri.TryCreate(dockerHostValue, UriKind.Absolute, out Uri? dockerHostUri))
        {
            host = dockerHostUri.Host;
        }
        else
        {
            int portSeparatorIndex = dockerHostValue.LastIndexOf(':');
            if (portSeparatorIndex > 0 && dockerHostValue.IndexOf(':') == portSeparatorIndex)
            {
                host = dockerHostValue[..portSeparatorIndex];
            }
        }

        if (string.IsNullOrWhiteSpace(host)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return host;
    }
}
