namespace Fabric.Packs.Tests;

[Collection(FixedPorts.Name)]
public sealed class ModuleSampleSqlServerTests
{
    /// <summary>The sample (#734) in a CRUD module on SQL Server.</summary>
    [Fact]
    public void A_new_modules_sample_passes_its_tests_and_is_created_and_listed_through_the_apphost() =>
        ModuleSampleBuild.PassesItsTestsAndRunsThroughTheAppHost("lean-crud", "backend.database=sqlserver");
}
