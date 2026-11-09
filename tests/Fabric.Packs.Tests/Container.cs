using System.Diagnostics;

namespace Fabric.Packs.Tests;

/// <summary>A throw-away Docker container on a random local port, removed on dispose.</summary>
internal sealed class Container : IDisposable
{
    private readonly string id;

    /// <summary>What the container needs and is removed with it: containers it depends on, then their network.</summary>
    private readonly string[] companions;

    private readonly string? network;

    private Container(string id, int port, string[]? companions = null, string? network = null, int? managementPort = null)
    {
        this.id = id;
        Port = port;
        this.companions = companions ?? [];
        this.network = network;
        ManagementPort = managementPort;
    }

    public string Id => id;

    public int Port { get; }

    /// <summary>The port of a broker's management API, where it has one apart from <see cref="Port"/>.</summary>
    public int? ManagementPort { get; }

    public static Container Postgres(string password)
    {
        var container = Start("postgres:18.3", 5432, [$"POSTGRES_PASSWORD={password}"]);
        container.WaitUntil("postgres", "pg_isready", "-U", "postgres");
        return container;
    }

    /// <summary>SQL Server, the image the AppHost runs; ready once sqlcmd can sign in as sa. The licence is accepted for this test only.</summary>
    public static Container SqlServer(string password)
    {
        var container = Start(SqlServerImage, 1433, ["ACCEPT_EULA=Y", $"MSSQL_SA_PASSWORD={password}"]);
        container.WaitUntil("mssql", [.. SqlCmd(password, "master"), "-Q", "select 1"]);
        return container;
    }

    public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    public static Container RabbitMq()
    {
        var container = Start("rabbitmq:4.3-management", 5672, []);
        // As the broker's own user: run as root, the check creates the Erlang cookie the broker then cannot read.
        container.WaitUntil("rabbitmq", "rabbitmq-diagnostics", "-q", "check_port_connectivity");
        return container;
    }

    /// <summary>A single KurrentDB node without TLS, as the AppHost runs it in development; ready once its HTTP port reports itself live.</summary>
    public static Container KurrentDb(string image)
    {
        var container = Start(image, 2113, ["KURRENTDB_INSECURE=true", "KURRENTDB_CLUSTER_SIZE=1"]);
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var attempt = 0; attempt < 90; attempt++)
        {
            try
            {
                if (client.GetAsync(new Uri($"http://127.0.0.1:{container.Port}/health/live")).GetAwaiter().GetResult().IsSuccessStatusCode)
                {
                    return container;
                }
            }
            catch (System.Net.Http.HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException)
            {
                // Listening, not answering yet.
            }

            Thread.Sleep(1000);
        }

        container.Dispose();
        throw new InvalidOperationException($"KurrentDB ({image}) did not become live.");
    }

    /// <summary>
    /// A single-node Kafka (the image Aspire runs). A client is told the broker's address by the broker itself, so it
    /// listens on the same port inside and outside the container and advertises that one.
    /// </summary>
    internal static Container Kafka(int? hostPort = null, string? name = null)
    {
        var port = hostPort ?? FreePort();
        var container = Start(
            "confluentinc/confluent-local:8.2.0",
            port,
            [
                $"KAFKA_LISTENERS=PLAINTEXT://localhost:29092,CONTROLLER://localhost:29093,PLAINTEXT_HOST://0.0.0.0:{port}",
                $"KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://localhost:29092,PLAINTEXT_HOST://localhost:{port}",
            ],
            port,
            name);
        container.WaitUntil("appuser", "kafka-topics", "--bootstrap-server", "localhost:29092", "--list");
        return container;
    }

    /// <summary>
    /// The Azure Service Bus emulator image the AppHost runs, with the SQL Server it keeps its entities in, on a network
    /// of their own. It takes messages on <see cref="Port"/> (AMQP) and management requests on
    /// <see cref="ManagementPort"/> (HTTP); ready once the management port reports it healthy. Both licences are
    /// accepted for this test only.
    /// </summary>
    internal static Container ServiceBus(int? amqpHostPort = null, string? name = null)
    {
        DockerPorts.EnsureRunsAlone();
        const string password = "Fabric-tests-1";
        var networkName = name ?? $"fabric-servicebus-{Guid.NewGuid():N}";
        var network = Docker("network", "create", networkName).Trim();
        List<string> started = [];
        Container container;
        try
        {
            started.Add(CreateAndStart(
                "--name", $"{networkName}-sqlserver", "--network", network, "--network-alias", "sqlserver",
                "--env", "ACCEPT_EULA=Y", "--env", $"MSSQL_SA_PASSWORD={password}",
                "mcr.microsoft.com/mssql/server:2022-latest"));
            started.Add(CreateAndStart(
                "--name", $"{networkName}-emulator", "--network", network,
                "--publish", $"127.0.0.1:{amqpHostPort}:5672", "--publish", "127.0.0.1::5300",
                "--env", "ACCEPT_EULA=Y", "--env", "SQL_SERVER=sqlserver", "--env", $"MSSQL_SA_PASSWORD={password}",
                "mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.0"));
            container = new Container(started[1], PublishedPort(started[1], 5672), [started[0]], network, PublishedPort(started[1], 5300));
        }
        catch (Exception)
        {
            // What started is removed with the network before the failure goes on: no Container owns it yet.
            if (started.Count > 0)
            {
                Run(["rm", "--force", "--volumes", .. started]);
            }

            Run(["network", "rm", network]);
            throw;
        }

        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var attempt = 0; attempt < 180; attempt++)
        {
            try
            {
                if (client.GetAsync(new Uri($"http://127.0.0.1:{container.ManagementPort}/health")).GetAwaiter().GetResult().IsSuccessStatusCode)
                {
                    return container;
                }
            }
            catch (System.Net.Http.HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException)
            {
                // Listening, not answering yet.
            }

            Thread.Sleep(1000);
        }

        var logs = Run(["logs", "--tail", "40", container.Id]);
        container.Dispose();
        throw new InvalidOperationException($"The Service Bus emulator did not become healthy:{Environment.NewLine}{logs.Output}{logs.Error}");
    }

    /// <summary>An emulator connection string to one of the emulator's ports, in the form Aspire gives the host.</summary>
    public static string ServiceBusConnectionString(int port) =>
        $"Endpoint=sb://localhost:{port};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    /// <summary>The emulator's queues, as its management API lists them: an Atom feed with one entry, titled by name, per queue.</summary>
    public string ServiceBusQueues()
    {
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        return client.GetStringAsync(new Uri($"http://127.0.0.1:{ManagementPort}/$Resources/queues?api-version=2021-05")).GetAwaiter().GetResult();
    }

    /// <summary>The first message of a Kafka topic, read with the broker's own console consumer; empty when none arrives in time.</summary>
    public string FirstKafkaMessage(string topic) =>
        Run(["exec", "--user", "appuser", id, "kafka-console-consumer", "--bootstrap-server", "localhost:29092", "--topic", topic,
            "--from-beginning", "--max-messages", "1", "--timeout-ms", "30000"]).Output;

    /// <summary>Runs one SQL statement as the postgres user and returns its unaligned, tuples-only output.</summary>
    public string Psql(string password, string sql) =>
        Docker("exec", "--env", $"PGPASSWORD={password}", id, "psql", "-U", "postgres", "-d", "postgres", "-tAc", sql).Trim();

    /// <summary>Runs one T-SQL batch as sa in <paramref name="database"/> and returns its output without headers, padding or row counts.</summary>
    public string Sql(string password, string sql, string database = "shop") =>
        Docker(["exec", "--user", "mssql", id, .. SqlCmd(password, database), "-h", "-1", "-W", "-b", "-Q", $"set nocount on; {sql}"]).Trim();

    private static string[] SqlCmd(string password, string database) =>
        ["/opt/mssql-tools18/bin/sqlcmd", "-C", "-S", "localhost", "-U", "sa", "-P", password, "-d", database];

    /// <summary>Removes the container with the anonymous volumes its image declares; without --volumes they outlive it.</summary>
    public void Dispose()
    {
        Docker(["rm", "--force", "--volumes", id, .. companions]);
        if (network is not null)
        {
            Docker("network", "rm", network);
        }
    }

    /// <summary>
    /// Starts <paramref name="image"/> with <paramref name="containerPort"/> published on a local port Docker picks, or on
    /// <paramref name="hostPort"/> when given.
    /// </summary>
    internal static Container Start(string image, int containerPort, string[] environment, int? hostPort = null, string? name = null)
    {
        DockerPorts.EnsureRunsAlone();
        List<string> arguments = ["--publish", $"127.0.0.1:{hostPort}:{containerPort}"];
        if (name is not null)
        {
            arguments.AddRange(["--name", name]);
        }

        foreach (var variable in environment)
        {
            arguments.AddRange(["--env", variable]);
        }

        arguments.Add(image);
        var id = CreateAndStart([.. arguments]);
        try
        {
            return new Container(id, PublishedPort(id, containerPort));
        }
        catch (InvalidOperationException)
        {
            // No Container owns it yet, so it is removed here, with its volumes, before the failure goes on.
            Run(["rm", "--force", "--volumes", id]);
            throw;
        }
    }

    /// <summary>Creates a container from <c>docker create</c>'s <paramref name="arguments"/>, image last, starts it and returns its id.</summary>
    /// <remarks>
    /// Created and started apart: a start that fails, at networking for instance, leaves a created container and its
    /// anonymous volumes, and only <c>docker create</c> says which container that is. It is removed with them before
    /// the failure goes on.
    /// </remarks>
    private static string CreateAndStart(params string[] arguments)
    {
        var id = Docker(["create", .. arguments]).Trim();
        try
        {
            Docker("start", id);
            return id;
        }
        catch (InvalidOperationException)
        {
            Run(["rm", "--force", "--volumes", id]);
            throw;
        }
    }

    /// <summary>The local port Docker published a container's port on.</summary>
    private static int PublishedPort(string id, int containerPort)
    {
        var mapping = Docker("port", id, containerPort.ToString(System.Globalization.CultureInfo.InvariantCulture)).Trim().Split('\n')[0];
        return int.Parse(mapping[(mapping.LastIndexOf(':') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private void WaitUntil(string user, params string[] command)
    {
        for (var attempt = 0; attempt < 90; attempt++)
        {
            if (TryDocker(["exec", "--user", user, id, .. command]))
            {
                return;
            }

            Thread.Sleep(1000);
        }

        throw new InvalidOperationException($"Container {id} did not become ready: {string.Join(' ', command)}");
    }

    /// <returns>Standard output only: docker writes pull progress to standard error, which must not end up in an id.</returns>
    private static string Docker(params string[] arguments)
    {
        var (exitCode, output, error) = Run(arguments);
        return exitCode == 0 ? output : throw new InvalidOperationException($"docker {string.Join(' ', arguments)} failed: {output}{error}");
    }

    private static bool TryDocker(string[] arguments) => Run(arguments).ExitCode == 0;

    private static (int ExitCode, string Output, string Error) Run(string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error.Result);
    }
}
