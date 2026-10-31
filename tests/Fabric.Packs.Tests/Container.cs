using System.Diagnostics;

namespace Fabric.Packs.Tests;

/// <summary>A throw-away Docker container on a random local port, removed on dispose.</summary>
internal sealed class Container : IDisposable
{
    private readonly string id;

    private Container(string id, int port)
    {
        this.id = id;
        Port = port;
    }

    public int Port { get; }

    public static Container Postgres(string password)
    {
        var container = Start("postgres:18.3", 5432, $"POSTGRES_PASSWORD={password}");
        container.WaitUntil("postgres", "pg_isready", "-U", "postgres");
        return container;
    }

    public static Container RabbitMq()
    {
        var container = Start("rabbitmq:4.3-management", 5672);
        // As the broker's own user: run as root, the check creates the Erlang cookie the broker then cannot read.
        container.WaitUntil("rabbitmq", "rabbitmq-diagnostics", "-q", "check_port_connectivity");
        return container;
    }

    /// <summary>
    /// A single-node Kafka (the image Aspire runs). A client is told the broker's address by the broker itself, so it
    /// listens on the same port inside and outside the container and advertises that one.
    /// </summary>
    public static Container Kafka()
    {
        var port = FreePort();
        var id = Docker(
            "run", "--detach", "--publish", $"127.0.0.1:{port}:{port}",
            "--env", $"KAFKA_LISTENERS=PLAINTEXT://localhost:29092,CONTROLLER://localhost:29093,PLAINTEXT_HOST://0.0.0.0:{port}",
            "--env", $"KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://localhost:29092,PLAINTEXT_HOST://localhost:{port}",
            "confluentinc/confluent-local:8.2.0").Trim();
        var container = new Container(id, port);
        container.WaitUntil("appuser", "kafka-topics", "--bootstrap-server", "localhost:29092", "--list");
        return container;
    }

    /// <summary>Runs one SQL statement as the postgres user and returns its unaligned, tuples-only output.</summary>
    public string Psql(string password, string sql) =>
        Docker("exec", "--env", $"PGPASSWORD={password}", id, "psql", "-U", "postgres", "-d", "postgres", "-tAc", sql).Trim();

    public void Dispose() => Docker("rm", "--force", id);

    private static Container Start(string image, int containerPort, params string[] environment)
    {
        List<string> arguments = ["run", "--detach", "--publish", $"127.0.0.1::{containerPort}"];
        foreach (var variable in environment)
        {
            arguments.AddRange(["--env", variable]);
        }

        arguments.Add(image);
        var id = Docker([.. arguments]).Trim();
        var mapping = Docker("port", id, containerPort.ToString(System.Globalization.CultureInfo.InvariantCulture)).Trim().Split('\n')[0];
        return new Container(id, int.Parse(mapping[(mapping.LastIndexOf(':') + 1)..], System.Globalization.CultureInfo.InvariantCulture));
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
