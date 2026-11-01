namespace Fabric.Packs.Tests;

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

    private static string[] VolumesOf(string containerId)
    {
        var (exitCode, output) = Docker("inspect", "--format", "{{range .Mounts}}{{if eq .Type \"volume\"}}{{.Name}} {{end}}{{end}}", containerId);
        exitCode.ShouldBe(0, output);
        return output.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static (int ExitCode, string Output) Docker(params string[] arguments) =>
        Shell.Run(Directory.GetCurrentDirectory(), "docker", arguments);
}
