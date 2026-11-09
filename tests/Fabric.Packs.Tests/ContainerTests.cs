using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Fabric.Packs.Tests;

[Collection(DockerPorts.Name)]
public sealed class ContainerTests
{
    [Fact]
    public void Disposing_a_container_removes_the_anonymous_volumes_attached_to_it()
    {
        var postgres = Container.Postgres("fabric-tests");
        string[] volumes;
        try
        {
            volumes = VolumesOf(postgres.Id);
        }
        finally
        {
            postgres.Dispose();
        }

        volumes.ShouldNotBeEmpty("postgres:18.3 declares /var/lib/postgresql, so its container has an anonymous volume");
        var left = volumes.Where(volume => Docker("volume", "inspect", volume).ExitCode == 0).ToArray();

        // Only this container's volumes, never the daemon's total: other runs share the daemon. Remove what is left so a failing run does not add to the pile.
        foreach (var volume in left)
        {
            Docker("volume", "rm", "--force", volume);
        }

        left.ShouldBeEmpty();
    }

    [Fact]
    public void A_container_whose_start_fails_at_networking_is_removed_with_its_anonymous_volumes()
    {
        var name = $"fabric-tests-failed-start-{Guid.NewGuid():N}";
        var since = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        using (var occupant = new TcpListener(IPAddress.Loopback, 0))
        {
            occupant.Start();
            var occupied = ((IPEndPoint)occupant.LocalEndpoint).Port;

            var exception = Should.Throw<InvalidOperationException>(() => Container.Start("postgres:18.3", 5432, ["POSTGRES_PASSWORD=fabric-tests"], occupied, name));
            exception.Message.ShouldContain("address already in use");
        }

        var created = Events(since, "--filter", "type=container", "--filter", "event=create", "--filter", $"container={name}", "--format", "{{.Actor.ID}}");
        created.ShouldHaveSingleItem("docker created the container before its networking failed");
        var volumes = Events(since, "--filter", "type=volume", "--filter", "event=mount", "--format", "{{.Actor.ID}} {{index .Actor.Attributes \"container\"}}")
            .Select(line => line.Split(' '))
            .Where(fields => fields.Length == 2 && fields[1] == created[0])
            .Select(fields => fields[0])
            .ToArray();
        volumes.ShouldNotBeEmpty("postgres:18.3 declares /var/lib/postgresql, so docker mounted an anonymous volume before networking");

        var containerLeft = Docker("inspect", "--type", "container", name).ExitCode == 0;
        var volumesLeft = volumes.Where(volume => Docker("volume", "inspect", volume).ExitCode == 0).ToArray();

        // Remove what is left so a failing run does not add to the pile.
        if (containerLeft)
        {
            Docker(["rm", "--force", "--volumes", name]);
        }

        foreach (var volume in volumesLeft)
        {
            Docker("volume", "rm", "--force", volume);
        }

        containerLeft.ShouldBeFalse($"container {name} is left behind");
        volumesLeft.ShouldBeEmpty();
    }

    /// <summary>The daemon's events since <paramref name="since"/> up to now, one formatted line each.</summary>
    private static string[] Events(string since, params string[] filterAndFormat)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var (exitCode, output) = Docker(["events", "--since", since, "--until", until, .. filterAndFormat]);
        exitCode.ShouldBe(0, output);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string[] VolumesOf(string containerId)
    {
        var (exitCode, output) = Docker("inspect", "--format", "{{range .Mounts}}{{if eq .Type \"volume\"}}{{.Name}} {{end}}{{end}}", containerId);
        exitCode.ShouldBe(0, output);
        return output.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static (int ExitCode, string Output) Docker(params string[] arguments) =>
        Shell.Run(Directory.GetCurrentDirectory(), "docker", arguments);
}
