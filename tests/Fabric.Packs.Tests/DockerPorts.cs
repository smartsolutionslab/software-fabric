namespace Fabric.Packs.Tests;

/// <summary>
/// Tests that start Docker containers with published ports, directly or through a generated project's Testcontainers
/// tests (#338). Docker picks a container's host port from the kernel's ephemeral range by its own count of the ports
/// it handed out, without asking the kernel which are free; a port that another test holds at that moment, such as an
/// in-process host listening on port 0 or the local end of a connection, fails the start with "address already in
/// use". So these tests run alone, after the parallel ones.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DockerPorts
{
    public const string Name = "Docker ports";

    /// <summary>Fails the calling test unless it runs alone: in this collection, or another that disables parallelisation.</summary>
    public static void EnsureRunsAlone()
    {
        if (TestContext.Current.TestCollection is not Xunit.v3.ICoreTestCollection { DisableParallelization: true })
        {
            throw new InvalidOperationException(
                $"{TestContext.Current.TestClass?.TestClassName} starts Docker containers while other test classes run, whose ports Docker may pick. Mark the class [Collection(DockerPorts.Name)].");
        }
    }
}
