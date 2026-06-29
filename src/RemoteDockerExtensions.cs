using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace AditiKraft.Aspire.Hosting.RemoteDocker;

public static class RemoteDockerExtensions
{
    public static RemoteDockerOptions AddRemoteDockerSshForwarding(
        this IDistributedApplicationBuilder builder,
        Action<RemoteDockerOptions>? configure = null)
    {
        // Honor an already-exported DOCKER_HOST env var; otherwise fall back to RemoteDocker:DockerHost
        // from config and publish it to the env var so Aspire's DCP talks to the remote daemon.
        string? configuredDockerHost = builder.Configuration[ConfigKeys.DockerHost];
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DockerHostEnvVar))
            && !string.IsNullOrWhiteSpace(configuredDockerHost))
        {
            Environment.SetEnvironmentVariable(DockerHostEnvVar, configuredDockerHost);
        }

        string? dockerHost = Environment.GetEnvironmentVariable(DockerHostEnvVar);
        string remoteHost = DockerHostHelper.GetRemoteDockerHostName(dockerHost);
        bool useRemote = !string.IsNullOrWhiteSpace(remoteHost);

        string? sshUser = builder.Configuration[ConfigKeys.SshUser];
        string? sshKeyFile = builder.Configuration[ConfigKeys.SshKeyFile];
        string? sshPassword = builder.Configuration[ConfigKeys.SshPassword];

        if (useRemote && string.IsNullOrWhiteSpace(sshUser))
        {
            throw new InvalidOperationException(
                "ssh_user must be configured when DOCKER_HOST points to a remote Docker host.");
        }

        if (useRemote && string.IsNullOrWhiteSpace(sshKeyFile) && string.IsNullOrWhiteSpace(sshPassword))
        {
            throw new InvalidOperationException(
                "Either ssh_key_file or ssh_password must be configured when DOCKER_HOST points to a remote Docker host.");
        }

        RemoteDockerOptions options = new(useRemote, remoteHost, sshUser, sshKeyFile, sshPassword);
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);

        // Connect before the orchestrator starts resources. Modern eventing replacement for the
        // obsolete IDistributedApplicationLifecycleHook. Connect() is idempotent, so an explicit
        // pre-Build call by the caller is also safe.
        builder.Eventing.Subscribe<BeforeStartEvent>((_, _) =>
        {
            options.Connect();
            return Task.CompletedTask;
        });

        // Dispose the tunnel on host shutdown, so the AppHost needs no try/finally of its own.
        builder.Services.AddHostedService<RemoteDockerForwarderService>();

        return options;
    }

    public static IResourceBuilder<TResource> ForwardViaSsh<TResource>(
        this IResourceBuilder<TResource> resourceBuilder,
        RemoteDockerOptions options)
        where TResource : IResource
    {
        if (!options.IsEnabled)
        {
            return resourceBuilder;
        }

        foreach (EndpointAnnotation endpoint in resourceBuilder.Resource.Annotations.OfType<EndpointAnnotation>())
        {
            int hostPort = endpoint.Port ?? endpoint.TargetPort
                ?? throw new InvalidOperationException(
                    $"Cannot determine port for endpoint '{endpoint.Name}' on resource '{resourceBuilder.Resource.Name}'. " +
                    "Call .WithHostPort(...) before .ForwardViaSsh(...) or ensure the resource has a target port.");

            options.AddPortMapping(hostPort, hostPort);
        }

        return resourceBuilder.DisableProxiedEndpointsWhen(true);
    }

    public static IResourceBuilder<TResource> DisableProxiedEndpointsWhen<TResource>(
        this IResourceBuilder<TResource> resourceBuilder,
        bool condition)
        where TResource : IResource
    {
        if (!condition)
        {
            return resourceBuilder;
        }

        foreach (EndpointAnnotation endpointAnnotation in resourceBuilder.Resource.Annotations
                     .OfType<EndpointAnnotation>())
        {
            endpointAnnotation.IsProxied = false;
            Console.WriteLine(
                $"Setting {resourceBuilder.Resource.Name} endpoint '{endpointAnnotation.Name}' to not be proxied");
        }

        return resourceBuilder;
    }

    private const string DockerHostEnvVar = "DOCKER_HOST";

    private static class ConfigKeys
    {
        private const string Section = "RemoteDocker";
        public const string DockerHost = $"{Section}:DockerHost";
        public const string SshUser = $"{Section}:SshUser";
        public const string SshKeyFile = $"{Section}:SshKeyFile";
        public const string SshPassword = $"{Section}:SshPassword";
    }
}
