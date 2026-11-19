using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #697: a route change is a view transition where the browser has the View Transitions API, through each router's
/// own way of asking for one (React Router's <c>viewTransition</c>, TanStack Router's <c>defaultViewTransition</c>,
/// <c>document.startViewTransition</c> around Vue Router's navigation); a browser without the API navigates as it did.
/// With <c>prefers-reduced-motion: reduce</c> the transition's animation is none, so the route change cuts. No
/// generated module has a detail page beside its list, so no element is shared between a row and a heading.
/// </summary>
public sealed partial class ViewTransitionsTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";
    private const string TanStackRouter = "frontend.routing=tanstack-router";

    /// <summary>Every routing pack, with each React Router branch of the app (shadcn's shell, MUI's bar, the plain bar).</summary>
    public static TheoryData<string, string[]> Routers => new()
    {
        { React, [] },
        { React, ["frontend.ui-library=mui", "frontend.styling=css-modules"] },
        { React, ["frontend.ui-library=radix-headless"] },
        { "lean-crud", [] },
        { React, [TanStackRouter] },
        { "lean-crud", [TanStackRouter] },
        { Vue, [] },
        { Vue, ["frontend.ui-library=reka-ui"] },
        { "industrial-event-driven-mfe", [] },
    };

    [Theory]
    [InlineData(React)]
    [InlineData(React, "frontend.ui-library=mui", "frontend.styling=css-modules")]
    [InlineData(React, "frontend.ui-library=radix-headless")]
    [InlineData("industrial-event-driven-mfe")]
    public void React_router_runs_a_data_router_whose_navigation_links_ask_for_a_view_transition(string profile, params string[] overrides)
    {
        var files = Files(profile, overrides);

        var router = files["web/app/src/providers/AppRouter.tsx"];
        router.ShouldContain("createBrowserRouter(");
        router.ShouldContain("<RouterProvider router=");
        files["web/app/src/providers/router.tsx"].ShouldNotContain("BrowserRouter>");
        var app = files["web/app/src/App.tsx"];
        NavigationLinks().Count(app).ShouldBeGreaterThan(0, app);
        foreach (Match link in NavigationLinks().Matches(app))
        {
            link.Value.ShouldContain("viewTransition", customMessage: $"{link.Value} in\n{app}");
        }
    }

    [Fact]
    public void With_shadcn_the_command_palette_and_the_breadcrumbs_of_react_router_ask_for_a_view_transition_too()
    {
        var files = Files(React);

        files["web/app/src/shell/CommandPalette.tsx"].ShouldContain("navigate(to, { viewTransition: true })");
        files["web/app/src/shell/Breadcrumbs.tsx"].ShouldContain("<Link to={crumb.to} viewTransition>");
    }

    [Theory]
    [InlineData(React)]
    [InlineData("lean-crud")]
    public void TanStack_router_asks_for_a_view_transition_on_every_navigation(string profile)
    {
        var files = Files(profile, TanStackRouter);

        files["web/app/src/router.tsx"].ShouldContain("defaultViewTransition: true");
    }

    [Fact]
    public void Vue_router_resolves_each_navigation_inside_document_start_view_transition_where_the_browser_has_it()
    {
        var files = Files(Vue);

        var router = files["web/app/src/router.ts"];
        router.ShouldContain("router.beforeResolve(");
        router.ShouldContain("document.startViewTransition(");
        router.ShouldContain("typeof document.startViewTransition !== 'function'");
    }

    [Theory]
    [MemberData(nameof(Routers))]
    public void With_reduced_motion_a_route_change_cuts(string profile, string[] overrides)
    {
        var files = Files(profile, overrides);

        var styles = files["web/app/src/index.css"];
        styles.ShouldContain(
            """
            @media (prefers-reduced-motion: reduce) {
              ::view-transition-group(*),
              ::view-transition-old(*),
              ::view-transition-new(*) {
                animation: none;
              }
            }
            """,
            Case.Sensitive,
            styles);
    }

    [Fact]
    public void The_routers_rules_say_how_a_route_change_animates_and_how_reduced_motion_stops_it()
    {
        foreach (var (profile, overrides) in new (string Profile, string[] Overrides)[] { (React, []), (React, [TanStackRouter]), (Vue, []) })
        {
            var agents = Files(profile, overrides)["AGENTS.md"];
            agents.ShouldContain("View Transitions API", customMessage: $"{profile} {string.Join(' ', overrides)}");
            agents.ShouldContain("view-transition-name", customMessage: $"{profile} {string.Join(' ', overrides)}");
        }
    }

    /// <summary>
    /// The web app builds, lints and formats with view transitions, and its tests navigate with and without the API:
    /// with a stand-in for <c>document.startViewTransition</c> the navigation runs inside it, and without one (jsdom has
    /// none) it reaches the page as before.
    /// </summary>
    [Theory]
    [InlineData(React, "", "src/App.test.tsx")]
    [InlineData(React, TanStackRouter, "src/App.test.tsx")]
    [InlineData(Vue, "", "src/App.test.ts")]
    public void The_web_app_navigates_in_a_view_transition_with_the_api_and_as_before_without_it(string profile, string routing, string testFile)
    {
        using var project = new GeneratedProject(profile, routing == string.Empty ? [] : [routing]);
        project.AddModule("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (stepExitCode, stepOutput) = Shell.Run(web, "pnpm", step);
            stepExitCode.ShouldBe(0, $"pnpm {step}:\n{stepOutput}");
        }

        var (formatExitCode, formatOutput) = Shell.Run(app, "pnpm", "format:check");
        formatExitCode.ShouldBe(0, $"pnpm format:check:\n{formatOutput}");

        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");

        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain($"✓ {testFile} > the Home link goes to the start page in a view transition where the browser has the API", customMessage: testOutput);
        plain.ShouldContain($"✓ {testFile} > without the View Transitions API the Home link goes to the start page as before", customMessage: testOutput);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>A link of the app's navigation: a NavLink, or MUI's Link drawing one.</summary>
    [GeneratedRegex(@"<(?:NavLink\b|Link component=\{NavLink\})[^>]*>")]
    private static partial Regex NavigationLinks();

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}
