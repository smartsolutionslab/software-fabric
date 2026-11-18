namespace Fabric.Packs.Tests;

[Collection(FixedPorts.Name)]
public sealed class ModuleSampleEventSourcedOnKurrentDbTests
{
    /// <summary>The sample (#734) in an event-sourced module on KurrentDB, whose command writes the samples' table.</summary>
    [Fact]
    public void A_new_modules_sample_passes_its_tests_and_is_created_and_listed_through_the_apphost() =>
        ModuleSampleBuild.PassesItsTestsAndRunsThroughTheAppHost("industrial-event-driven-mfe", "module.event-store=kurrentdb", "module.cqrs=logical");
}
