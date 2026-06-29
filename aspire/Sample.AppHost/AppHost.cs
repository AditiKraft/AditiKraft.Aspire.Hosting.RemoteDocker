using AditiKraft.Aspire.Hosting.RemoteDocker;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Host ports for the remote Docker SSH tunnel. Each must be FREE on the remote host —
// default ports (1433, 5432, ...) often collide on a shared host (see the RemoteDocker README).
const int SqlServerHostPort = 4545;
const int PostgresHostPort = 54320;
const int PgAdminHostPort = 62897;

RemoteDockerOptions remoteDocker = builder.AddRemoteDockerSshForwarding();

// SQL Server
IResourceBuilder<ParameterResource> sqlPassword = builder.AddParameter("MSSQLSAPASSWORD", true);
IResourceBuilder<SqlServerServerResource> sql = builder
    .AddSqlServer("SqlServerDatabase", sqlPassword, SqlServerHostPort)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume(isReadOnly: false)
    .WithImageTag("2025-latest")
    .ForwardViaSsh(remoteDocker);

IResourceBuilder<SqlServerDatabaseResource> sqlServerDatabase = sql.AddDatabase("sample-db");

// Postgres
IResourceBuilder<ParameterResource> postgresUsername = builder.AddParameter("postgresUsername", true);
IResourceBuilder<ParameterResource> postgresPassword = builder.AddParameter("postgresPassword", true);

IResourceBuilder<PostgresServerResource> databaseServer = builder.AddPostgres("postgres", postgresUsername, postgresPassword)
    .WithDataVolume(isReadOnly: false)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithContainerName("postgres")
    .WithHostPort(PostgresHostPort)
    .WithPgAdmin(pgAdmin =>
    {
        if (remoteDocker.IsEnabled)
        {
            // PgAdmin's container listens on port 80, which can't be used as a host port on
            // Windows (reserved by http.sys). Pin a fixed, free high port so the SSH tunnel has
            // a deterministic target.
            pgAdmin.WithHostPort(PgAdminHostPort).ForwardViaSsh(remoteDocker);
        }
    })
    .ForwardViaSsh(remoteDocker);

IResourceBuilder<PostgresDatabaseResource> database = databaseServer.AddDatabase("appDb");

builder.AddProject<Projects.Sample>("sample")
    .WaitForStart(sqlServerDatabase)
    .WithReference(sqlServerDatabase);

builder.Build().Run();
