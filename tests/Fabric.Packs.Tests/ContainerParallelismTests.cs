namespace Fabric.Packs.Tests;

/// <summary>Runs in parallel with other classes, as a test class does unless a collection says otherwise.</summary>
public sealed class ContainerParallelismTests
{
    [Fact]
    public void A_test_that_runs_in_parallel_with_others_cannot_start_a_container()
    {
        var exception = Should.Throw<InvalidOperationException>(() => Container.Postgres("fabric-tests").Dispose());

        exception.Message.ShouldContain("[Collection(DockerPorts.Name)]");
    }
}
