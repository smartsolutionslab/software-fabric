using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #736: with shadcn and TanStack Router in a single-page app, the app shell of #695 is built from the router's route
/// tree: shadcn's collapsible sidebar holding the start page and every module's route, with the theme toggle and the
/// user menu; breadcrumbs from the route matches; a command palette on Ctrl+K or Cmd+K that the keyboard alone drives;
/// and the toaster with <c>notify</c>. The shell has its own axe check. The micro-frontend shell and the other UI
/// libraries keep their navigation bar.
/// </summary>
public sealed partial class TanStackAppShellTests
{
    private const string React = "saas-standard-react";
    private const string TanStackRouter = "frontend.routing=tanstack-router";

    /// <summary>A project of the shell by API client: how the user menu asks /me differs by client.</summary>
    public static TheoryData<string, string[], string> Clients => new()
    {
        { React, [TanStackRouter], "useGetMeQuery()" },
        { "lean-crud", [TanStackRouter], "useGetMe()" },
        { "lean-crud", [TanStackRouter, "frontend.api-client=openapi-fetch"], "api.GET('/me')" },
    };

    /// <summary>The projects whose web app is built and tested: one per client, and one without sign-in.</summary>
    public static TheoryData<string, string[]> Builds => new()
    {
        { React, [TanStackRouter] },
        { "lean-crud", [TanStackRouter] },
        { "lean-crud", [TanStackRouter, "frontend.api-client=openapi-fetch"] },
        { "lean-crud", [TanStackRouter, "backend.auth=none"] },
    };

    [Fact]
    public void The_shell_is_a_collapsible_sidebar_of_the_route_tree_with_breadcrumbs_from_the_matches_a_command_palette_and_toasts()
    {
        var files = Files(React, TanStackRouter);

        var shell = files["web/app/src/App.tsx"];
        shell.ShouldContain("<SidebarProvider>");
        shell.ShouldContain("<SidebarTrigger");
        shell.ShouldContain("<SidebarRail />");
        shell.ShouldContain("<nav aria-label=\"Modules\">");
        shell.ShouldContain("<Breadcrumbs />");
        shell.ShouldContain("<CommandPalette");
        shell.ShouldContain("<Toaster />");
        shell.ShouldContain("<ThemeToggle />");
        shell.ShouldContain("<UserMenu />");
        shell.ShouldContain("<Outlet />");
        shell.ShouldContain("from '@tanstack/react-router'");
        shell.ShouldNotContain("import.meta.glob");
        files["web/app/src/shell/Breadcrumbs.tsx"].ShouldContain("useMatches(");
        files["web/app/src/shell/CommandPalette.tsx"].ShouldContain("from '@tanstack/react-router'");
        files.Keys.ShouldContain("web/app/src/shell/AppShell.test.tsx");
        files.Keys.ShouldContain("web/app/src/shell/AppShell.axe.test.tsx");
        files.Keys.ShouldContain("web/app/src/lib/notify.tsx");
        files.Keys.ShouldContain("web/app/src/lib/notify.test.tsx");
        files.Keys.ShouldContain("web/app/src/auth/UserMenu.tsx");
        files.Keys.ShouldContain("web/app/src/auth/UserMenu.test.tsx");
        files.Keys.ShouldContain("web/app/src/components/ui/sidebar.tsx");
        files.Keys.ShouldContain("web/app/src/components/ui/command.tsx");
        files.Keys.ShouldContain("web/app/src/components/ui/breadcrumb.tsx");
        files.Keys.ShouldContain("web/app/src/components/ui/sonner.tsx");
        var package = files["web/app/package.json"];
        package.ShouldContain("\"cmdk\": \"1.1.1\"");
        package.ShouldContain("\"sonner\": \"2.0.8\"");
        package.ShouldContain("\"@radix-ui/react-dialog\": \"1.3.1\"");
        files["web/app/src/test-setup.ts"].ShouldContain("ResizeObserver");
        files.Keys.ShouldNotContain(path => path.Contains("react-router", StringComparison.Ordinal));
        package.ShouldNotContain("\"react-router\":");
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public void The_user_menu_names_the_user_as_me_answers_and_the_sign_in_draws_no_bar_of_its_own(string profile, string[] overrides, string call)
    {
        var files = Files(profile, overrides);

        files["web/app/src/api/useCurrentUser.ts"].ShouldContain(call);
        files["web/app/src/auth/SignedIn.tsx"].ShouldNotContain("Signed in as");
        files["web/app/src/auth/SignedIn.tsx"].ShouldNotContain("signoutRedirect()");
        files["web/app/src/App.tsx"].ShouldContain("<UserMenu />");
    }

    [Fact]
    public void Without_sign_in_the_shell_has_no_user_menu()
    {
        var files = Files("lean-crud", TanStackRouter, "backend.auth=none");

        files["web/app/src/App.tsx"].ShouldContain("<SidebarProvider>");
        files["web/app/src/App.tsx"].ShouldNotContain("UserMenu");
        files.Keys.ShouldNotContain("web/app/src/api/useCurrentUser.ts");
    }

    [Fact]
    public void With_msal_the_shell_has_no_user_menu_and_the_sign_in_keeps_its_bar()
    {
        var files = Files(React, TanStackRouter, "backend.auth=entra-id", "frontend.auth-client=msal");

        files["web/app/src/App.tsx"].ShouldContain("<SidebarProvider>");
        files["web/app/src/App.tsx"].ShouldNotContain("UserMenu");
        files["web/app/src/auth/SignedIn.tsx"].ShouldContain("Signed in as");
    }

    /// <summary>The module page's sample form (#734) reports what creating a sample did in the shell's toasts.</summary>
    [Theory]
    [InlineData(React)]
    [InlineData("lean-crud")]
    public void The_module_page_reports_a_created_sample_through_notify(string profile)
    {
        using var project = new GeneratedProject(profile, TanStackRouter);
        project.AddModule("billing");
        var page = File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "src", "modules", "billing", "BillingPage.tsx"));

        page.ShouldContain("import { notify } from '@/lib/notify';");
        page.ShouldContain("notify.success(`The sample ${name} was created.`);");
        page.ShouldNotContain("useState");
    }

    /// <summary>Out of scope: with TanStack Router these keep the navigation bar they had, and get none of the shell's files.</summary>
    [Theory]
    [InlineData("industrial-event-driven-mfe", TanStackRouter)]
    [InlineData(React, TanStackRouter, "frontend.ui-library=radix-headless")]
    [InlineData(React, TanStackRouter, "frontend.ui-library=mui", "frontend.styling=css-modules")]
    public void The_micro_frontend_shell_and_the_other_ui_libraries_keep_their_navigation_bar(string profile, params string[] overrides)
    {
        var files = Files(profile, overrides);

        files["web/app/src/App.tsx"].ShouldNotContain("Sidebar");
        files.Keys.ShouldNotContain(path => path.StartsWith("web/app/src/shell/", StringComparison.Ordinal));
        files.Keys.ShouldNotContain("web/app/src/lib/notify.tsx");
        files.Keys.ShouldNotContain("web/app/src/api/useCurrentUser.ts");
        files.Keys.ShouldNotContain(path => path.StartsWith("web/app/src/auth/UserMenu.", StringComparison.Ordinal));
    }

    /// <summary>
    /// The web app builds, lints and formats with the shell, whichever client asks /me, and its tests check the sidebar,
    /// the breadcrumbs from the route matches, the command palette by keyboard alone, the toasts' roles, the user menu
    /// and the shell with axe.
    /// </summary>
    [Theory]
    [MemberData(nameof(Builds))]
    public void The_shell_builds_and_its_tests_pass(string profile, string[] overrides)
    {
        var signedIn = !overrides.Contains("backend.auth=none");
        using var project = new GeneratedProject(profile, overrides);
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
        List<string> passed =
        [
            "src/shell/AppShell.test.tsx > the sidebar lists the start page and every module from the route tree",
            "src/shell/AppShell.test.tsx > the sidebar collapses from its trigger and opens again",
            "src/shell/AppShell.test.tsx > on the start page the breadcrumbs name only it",
            "src/shell/AppShell.test.tsx > the breadcrumbs follow the route matches from the start page to a module's page and below it",
            "src/shell/AppShell.test.tsx > the page the user is on is text in the breadcrumbs, not a second link to it",
            "src/shell/AppShell.test.tsx > Ctrl+K opens the command palette and Escape closes it",
            "src/shell/AppShell.test.tsx > Cmd+K opens the command palette and Escape closes it",
            "src/shell/AppShell.test.tsx > the keyboard alone goes to a page from the command palette",
            "src/shell/AppShell.test.tsx > typing narrows the command palette to the pages that match",
            "src/shell/AppShell.axe.test.tsx > the app shell has no accessibility violations axe can find in the light theme",
            "src/shell/AppShell.axe.test.tsx > the app shell has no accessibility violations axe can find in the dark theme",
            "src/shell/AppShell.axe.test.tsx > the open command palette has no accessibility violations axe can find in the light theme",
            "src/shell/AppShell.axe.test.tsx > the open command palette has no accessibility violations axe can find in the dark theme",
            "src/shell/AppShell.axe.test.tsx > the collapsed sidebar leaves no accessibility violations axe can find",
            "src/lib/notify.test.tsx > a mutation's success shows as a status message",
            "src/lib/notify.test.tsx > a mutation's failure shows as an alert",
            "src/App.test.tsx > the start page names the application",
        ];
        if (signedIn)
        {
            passed.Add("src/auth/UserMenu.test.tsx > the user menu names the signed-in user as /me does");
            passed.Add("src/auth/UserMenu.test.tsx > Sign out signs the user out at the identity provider");
        }

        foreach (var name in passed)
        {
            plain.ShouldContain($"✓ {name}", customMessage: testOutput);
        }
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}
