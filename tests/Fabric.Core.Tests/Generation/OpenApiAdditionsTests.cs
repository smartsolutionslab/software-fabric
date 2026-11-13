using System.Text.Json.Nodes;
using Fabric.Core.Generation;

namespace Fabric.Core.Tests.Generation;

/// <summary>
/// What add-module adds to the frontend's copy of the OpenAPI document (ADR-0005, amended for #609): the new module's
/// paths where the host serves them, before the first path of a module the host maps after it, and its schemas in name
/// order; never a path or schema the copy already has.
/// </summary>
public class OpenApiAdditionsTests
{
    private const string Copy = """
        {
          "openapi": "3.1.2",
          "paths": {
            "/me": {
              "get": {}
            },
            "/orders/permissions/manage": {
              "get": {}
            },
            "/orders": {
              "get": {}
            }
          },
          "components": {
            "schemas": {
              "CurrentUser": {
                "type": "object"
              },
              "OrderDto": {
                "type": "object"
              }
            }
          }
        }

        """;

    private static readonly OpenApiAdditions Billing = new(
        [new("/billing/permissions/manage", JsonNode.Parse("""{ "get": { "tags": [ "Host" ] } }""")!)],
        [new("PermissionDto", JsonNode.Parse("""{ "type": "object" }""")!)],
        ["/orders"]);

    [Fact]
    public void Inserts_the_paths_before_the_first_path_of_a_module_mapped_after_it_and_the_schemas_in_name_order()
    {
        Billing.InsertInto(Copy, out var extended).ShouldBeNull();

        var document = JsonNode.Parse(extended)!;
        document["paths"]!.AsObject().Select(path => path.Key)
            .ShouldBe(["/me", "/billing/permissions/manage", "/orders/permissions/manage", "/orders"]);
        document["components"]!["schemas"]!.AsObject().Select(schema => schema.Key).ShouldBe(["CurrentUser", "OrderDto", "PermissionDto"]);
    }

    [Fact]
    public void Writes_the_copy_as_the_generated_integration_test_does_indented_with_a_final_newline()
    {
        Billing.InsertInto(Copy, out var extended).ShouldBeNull();

        extended.ShouldContain("""
                "/billing/permissions/manage": {
                  "get": {
                    "tags": [
                      "Host"
                    ]
                  }
                },
            """.ReplaceLineEndings("\n"));
        extended.ShouldEndWith("}\n");
        extended.ShouldNotContain("\r");
    }

    [Fact]
    public void Appends_the_paths_when_no_module_is_mapped_after_the_new_one()
    {
        var zeta = new OpenApiAdditions([new("/zeta/permissions/manage", JsonNode.Parse("{}")!)], [], []);

        zeta.InsertInto(Copy, out var extended).ShouldBeNull();

        JsonNode.Parse(extended)!["paths"]!.AsObject().Select(path => path.Key).Last().ShouldBe("/zeta/permissions/manage");
    }

    [Fact]
    public void A_later_modules_prefix_matches_its_own_paths_only()
    {
        // /ordersarchive is not a path of the module orders.
        var copy = Copy.Replace("\"/orders\": {", "\"/ordersarchive\": {", StringComparison.Ordinal)
            .Replace("\"/orders/permissions/manage\"", "\"/ordersarchive/permissions/manage\"", StringComparison.Ordinal);

        Billing.InsertInto(copy, out var extended).ShouldBeNull();

        JsonNode.Parse(extended)!["paths"]!.AsObject().Select(path => path.Key).Last().ShouldBe("/billing/permissions/manage");
    }

    [Fact]
    public void Refuses_a_path_the_copy_already_has_naming_it()
    {
        var copy = Copy.Replace("/orders/permissions/manage", "/billing/permissions/manage", StringComparison.Ordinal);

        Billing.InsertInto(copy, out _)
            .ShouldBe("web/app/openapi.json already has the path /billing/permissions/manage; add-module only adds a new module's paths and schemas.");
    }

    [Fact]
    public void Refuses_a_schema_the_copy_already_has_naming_it()
    {
        var copy = Copy.Replace("OrderDto", "PermissionDto", StringComparison.Ordinal);

        Billing.InsertInto(copy, out _)
            .ShouldBe("web/app/openapi.json already has the schema PermissionDto; add-module only adds a new module's paths and schemas.");
    }

    [Fact]
    public void Adds_the_components_a_copy_without_schemas_lacks()
    {
        var copy = """
            {
              "openapi": "3.1.2",
              "paths": {}
            }

            """;

        Billing.InsertInto(copy, out var extended).ShouldBeNull();

        var document = JsonNode.Parse(extended)!;
        document["paths"]!.AsObject().Select(path => path.Key).ShouldBe(["/billing/permissions/manage"]);
        document["components"]!["schemas"]!.AsObject().Select(schema => schema.Key).ShouldBe(["PermissionDto"]);
    }

    [Fact]
    public void Adds_only_the_schemas_to_components_that_have_none_keeping_what_components_already_holds()
    {
        var copy = """
            {
              "openapi": "3.1.2",
              "paths": {},
              "components": {
                "securitySchemes": {
                  "Bearer": {
                    "type": "http",
                    "scheme": "bearer"
                  }
                },
                "responses": {
                  "Forbidden": {
                    "description": "The caller lacks the policy."
                  }
                }
              }
            }

            """;

        Billing.InsertInto(copy, out var extended).ShouldBeNull();

        var components = JsonNode.Parse(extended)!["components"]!.AsObject();
        components.Select(member => member.Key).ShouldBe(["securitySchemes", "responses", "schemas"]);
        components["securitySchemes"]!["Bearer"]!["scheme"]!.GetValue<string>().ShouldBe("bearer");
        components["responses"]!["Forbidden"]!["description"]!.GetValue<string>().ShouldBe("The caller lacks the policy.");
        components["schemas"]!.AsObject().Select(schema => schema.Key).ShouldBe(["PermissionDto"]);
    }

    [Theory]
    [InlineData("""{ "paths": [] }""", "paths")]
    [InlineData("""{ "paths": {}, "components": [] }""", "components")]
    [InlineData("""{ "paths": {}, "components": { "schemas": 1 } }""", "components.schemas")]
    public void Refuses_a_copy_whose_paths_or_schemas_are_not_objects_naming_the_member(string copy, string member)
    {
        Billing.InsertInto(copy, out _)
            .ShouldBe($"web/app/openapi.json has a {member} that is not a JSON object; add-module adds the new module's paths to it.");
    }
}
