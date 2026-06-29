# AditiKraft.Aspire.Hosting.RemoteDocker

Aspire hosting extension that enables SSH port forwarding to a remote Docker host, so containers running on a remote machine appear as if they're running locally.

## What it does

When `DOCKER_HOST` points to a remote Docker host (e.g. `ssh://user@remote-server:2375`), Aspire will run containers on that remote host. This package:

1. Parses the `DOCKER_HOST` value to detect whether it's remote
2. Opens an SSH tunnel to the remote host
3. Forwards the necessary local ports to the remote Docker host ports
4. Disables Aspire's endpoint proxying for forwarded resources (so connections go directly through the tunnel)

It is **image-agnostic** — `ForwardViaSsh` works on *any* container resource (Postgres, SQL Server, Redis, RabbitMQ, your own `AddContainer`, …). It reads whatever endpoints the resource declares; there is no per-image logic.

When `DOCKER_HOST` is local (`npipe://`, `unix://`, `localhost`, or unset), all of the above is a **no-op** — Aspire behaves exactly as it normally would.

## Installation

Add a `ProjectReference` (in-repo) or `PackageReference` (published) to your AppHost project:

```xml
<ProjectReference Include="..\AditiKraft.Aspire.Hosting.RemoteDocker\AditiKraft.Aspire.Hosting.RemoteDocker.csproj"
                  IsAspireProjectResource="false" />
```

> `IsAspireProjectResource="false"` is required so the Aspire AppHost SDK treats this as a regular library reference, not an Aspire resource.
>
> **Project-reference caveat:** the Aspire AppHost SDK flattens an `IsAspireProjectResource="false"` project reference to a bare assembly reference and drops its transitive NuGet graph, so `SSH.NET` won't be copied to your AppHost output. Add it directly to the AppHost while using a project reference:
> ```xml
> <PackageReference Include="SSH.NET" Version="2025.1.0" />
> ```
> This is **not** needed when you consume the published NuGet package — `SSH.NET` flows in as a normal transitive dependency.

## Configuration

Put these under a `RemoteDocker` section in user secrets or `appsettings.json` on the AppHost project:

| Key                        | Required    | Description                                          |
|----------------------------|-------------|------------------------------------------------------|
| `RemoteDocker:DockerHost`  | No          | Docker host URL (e.g. `ssh://user@host:2375`). If local or unset, forwarding is disabled. An exported `DOCKER_HOST` env var takes precedence. |
| `RemoteDocker:SshUser`     | When remote | SSH username for the remote Docker host.             |
| `RemoteDocker:SshKeyFile`  | When remote | Path to SSH private key file.                        |
| `RemoteDocker:SshPassword` | When remote | SSH password (alternative to key file).              |

At least one of `SshKeyFile` or `SshPassword` must be configured when remote.

```bash
dotnet user-secrets set "RemoteDocker:DockerHost" "ssh://deploy@my-remote-host:2375"
dotnet user-secrets set "RemoteDocker:SshUser" "deploy"
dotnet user-secrets set "RemoteDocker:SshKeyFile" "C:/Users/me/.ssh/id_rsa"
```

Or as a section in `appsettings.json` (keep real credentials in **user secrets**, not committed config):

```json
{
  "RemoteDocker": {
    "DockerHost": "ssh://user@remote-host:2375",
    "SshUser": "user",
    "SshKeyFile": "",
    "SshPassword": "<ssh-password-or-blank-when-using-a-key>"
  }
}
```

## Usage

### Basic setup

```csharp
using AditiKraft.Aspire.Hosting.RemoteDocker;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Register the forwarder (reads config, validates SSH creds)
RemoteDockerOptions remoteDocker = builder.AddRemoteDockerSshForwarding();

// Attach to any container resource — works for any image.
// Pin a host port that is free on the remote host (see "Host ports" below).
builder.AddPostgres("postgres", username, password)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithHostPort(54320)
    .ForwardViaSsh(remoteDocker);

builder.AddSqlServer("sql", password).WithHostPort(14330).ForwardViaSsh(remoteDocker);
builder.AddRedis("redis").WithHostPort(63790).ForwardViaSsh(remoteDocker);

// Bring the tunnel up *before* the orchestrator starts resources / health checks.
// (AddRemoteDockerSshForwarding also auto-connects via the BeforeStartEvent, but the
//  explicit call here guarantees the proven timing and is idempotent.)
remoteDocker.Connect();
builder.Build().Run();
```

### With PgAdmin (or other child resources)

A child resource (like PgAdmin) usually has no fixed host port, so its endpoint would otherwise land on its container's privileged target port (e.g. 80, which `http.sys` reserves on Windows). Pin a real host port with `.WithHostPort(...)` **before** `.ForwardViaSsh(...)`:

```csharp
builder.AddPostgres("postgres", username, password)
    .WithPgAdmin(pgAdmin => pgAdmin.WithHostPort(62897).ForwardViaSsh(remoteDocker))
    .ForwardViaSsh(remoteDocker);
```

### How the forwarded port is chosen

`ForwardViaSsh` reads each endpoint's `Port ?? TargetPort` and forwards `localhost:<port> → remote:<port>` on that value. The remote Docker host publishes the (proxyless) container on that port, and the tunnel connects to it there.

### Host ports must be free on the remote Docker host

> [!IMPORTANT]
> The forwarded port has to be **unused on the remote Docker host**, not just on your local
> machine. If something else on the remote host already owns that port (e.g. another project
> already runs Postgres on `5432` or SQL Server on `1433`), your container won't be published
> there and **the tunnel silently connects to that other container instead of yours**.
>
> Symptoms:
> - the resource shows **Unhealthy** because the credentials/database don't match the other container, or
> - worse, it shows **healthy** while you're actually talking to the wrong container.
>
> On a **shared** remote host this is easy to hit, because default container ports
> (`5432`, `1433`, `6379`, …) are exactly the ones likely to already be taken. Pin a unique,
> unused host port per resource with `.WithHostPort(...)` **before** `.ForwardViaSsh(...)`:
>
> ```csharp
> builder.AddSqlServer("sql", password).WithHostPort(14330).ForwardViaSsh(remoteDocker);
> builder.AddPostgres("postgres", user, pass).WithHostPort(54320).ForwardViaSsh(remoteDocker);
> ```
>
> To check what's already bound on the remote host:
> `DOCKER_HOST=<your-host> docker ps --format '{{.Names}}\t{{.Ports}}'`

### Escape hatch: manual port mapping

For ports that aren't Aspire resource endpoints:

```csharp
remoteDocker.AddPortMapping(8080, 8080);
```

## API Reference

| Member | Description |
|--------|-------------|
| `builder.AddRemoteDockerSshForwarding(Action<RemoteDockerOptions>? configure)` | Reads `DOCKER_HOST`/SSH config, validates, subscribes to `BeforeStartEvent` for auto-connect. Returns `RemoteDockerOptions`. |
| `resource.ForwardViaSsh(RemoteDockerOptions)` | Registers an SSH forward for each endpoint and disables endpoint proxying. No-op when local. |
| `remoteDocker.Connect()` | Opens the SSH tunnel and starts all forwards. Idempotent. Call right before `builder.Build().Run()`. |
| `remoteDocker.Dispose()` | Stops forwards and disconnects SSH. |
| `remoteDocker.AddPortMapping(int public, int private, bool forward = true)` | Manually register a port to forward. Returns `PortMap`. |
| `remoteDocker.IsEnabled` | `true` when `DOCKER_HOST` points to a remote host. |
| `remoteDocker.PortMappings` | `IReadOnlyList<PortMap>` of all registered forwards. |
| `.DisableProxiedEndpointsWhen(bool)` | Sets `IsProxied = false` on all endpoint annotations when condition is true. |

## How it works

1. **Build phase**: `AddRemoteDockerSshForwarding` reads config, registers `RemoteDockerOptions` as a DI singleton, and subscribes to `BeforeStartEvent`. `ForwardViaSsh` reads each resource's `EndpointAnnotation` to determine the forwarded port and adds a port mapping to the options.

2. **Connect**: `remoteDocker.Connect()` (called explicitly before `Build().Run()`, or automatically from `BeforeStartEvent`) creates an `SshPortForwarder`, connects to the remote host, and starts local port forwards for each registered `PortMap`. It's idempotent, so doing both is safe — whichever runs first wins.

3. **Shutdown**: a hosted service registered by `AddRemoteDockerSshForwarding` disposes the tunnel on host shutdown (stops forwards, disconnects SSH) — no `try/finally` needed in the AppHost. `remoteDocker.Dispose()` is still public if you want to tear it down manually.

## Local vs remote behavior

| Scenario | `IsEnabled` | `ForwardViaSsh` | Port forwarding | Endpoint proxying |
|----------|-------------|-----------------|-----------------|-------------------|
| Local Docker | `false` | no-op | none | normal (Aspire proxies) |
| Remote Docker | `true` | reads endpoint ports, adds forwards | SSH tunnel | disabled (direct connection) |

This means the **same AppHost code works for both local and remote** — just change the `DOCKER_HOST` config.
