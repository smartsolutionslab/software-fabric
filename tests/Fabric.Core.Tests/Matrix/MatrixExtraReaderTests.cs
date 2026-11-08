using Fabric.Core.Catalog;
using Fabric.Core.Matrix;

namespace Fabric.Core.Tests.Matrix;

public class MatrixExtraReaderTests
{
    private static IReadOnlyList<MatrixExtra> Read(string text) => MatrixExtraReader.Read(new StringReader(text), "matrix-extra.yaml");

    [Fact]
    public void Reads_each_combination_with_its_reason_and_choices()
    {
        var extras = Read("""
            combinations:
              - reason: event sourcing with grpc and roles
                choices:
                  module.style: event-sourced
                  backend.api-protocols: [rest, grpc]
                  backend.authorization: roles
            """);

        var extra = extras.ShouldHaveSingleItem();
        extra.Reason.ShouldBe("event sourcing with grpc and roles");
        extra.Choices.Keys.ShouldBe(["module.style", "backend.api-protocols", "backend.authorization"]);
        extra.Choices["backend.api-protocols"].ShouldBe(["rest", "grpc"]);
        extra.Choices["backend.authorization"].ShouldBe(["roles"]);
    }

    [Fact]
    public void An_empty_list_means_no_extra_combinations()
    {
        Read("combinations: []\n").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("combinations:\n  - reason: x\n    choices: { a.b: c }\n    issue: 12\n", "matrix-extra.yaml:4: unknown field 'issue' on extra combination 1; allowed: reason, choices.")]
    [InlineData("combinations:\n  - choices: { a.b: c }\n", "matrix-extra.yaml:2: missing field 'reason' on extra combination 1.")]
    [InlineData("extras: []\n", "matrix-extra.yaml:1: unknown field 'extras' on the extra combinations; allowed: combinations.")]
    public void A_malformed_file_is_rejected_with_its_line(string text, string message)
    {
        Should.Throw<CatalogException>(() => Read(text)).Message.ShouldBe(message);
    }
}
