namespace AditiKraft.Aspire.Hosting.RemoteDocker;

public sealed record PortMap(int PublicPort, int PrivatePort, bool PortForward = true);
