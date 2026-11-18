using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #695: with shadcn and react-router, or shadcn-vue and vue-router, in a single-page app, the app shell is shadcn's
/// collapsible sidebar holding every module's page from the routes the module registers, the theme toggle and the user
/// menu (the name /me answers, and Sign out); breadcrumbs from the route; a command palette on Ctrl+K or Cmd+K that the
/// keyboard alone drives; and toasts (sonner, vue-sonner) through which a mutation's result is a status message or an
/// alert. The shell has its own axe check. TanStack Router, the micro-frontend shell, the other UI libraries and MSAL's
/// user menu are out of scope: their output is unchanged.
/// </summary>
public sealed partial class AppShellTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";

    public static TheoryData<string, string, string> Shells => new()
    {
        { React, "web/app/src/App.tsx", "tsx" },
        { Vue, "web/app/src/App.vue", "vue" },
    };

    /// <summary>A project of each shell, by API client and state: how the user menu asks /me differs by both.</summary>
    public static TheoryData<string, string[], string> Clients => new()
    {
        { React, [], "useGetMeQuery()" },
        { "lean-crud", [], "useGetMe()" },
        { "lean-crud", ["frontend.api-client=openapi-fetch"], "api.GET('/me')" },
        { Vue, [], "useGetMe()" },
        { Vue, ["frontend.state=pinia"], "getMe()" },
        { Vue, ["frontend.api-client=openapi-fetch"], "api.GET('/me')" },
        { Vue, ["frontend.api-client=openapi-fetch", "frontend.state=pinia"], "api.GET('/me')" },
    };

    /// <summary>The same projects, for the runs that build them.</summary>
    public static TheoryData<string, string[]> Builds => new()
    {
        { React, [] },
        { "lean-crud", [] },
        { "lean-crud", ["frontend.api-client=openapi-fetch"] },
        { Vue, [] },
        { Vue, ["frontend.state=pinia"] },
        { Vue, ["frontend.api-client=openapi-fetch"] },
        { Vue, ["frontend.api-client=openapi-fetch", "frontend.state=pinia"] },
    };

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_shell_is_a_collapsible_sidebar_of_the_module_routes_with_breadcrumbs_a_command_palette_and_toasts(string profile, string app, string extension)
    {
        var files = Files(profile);

        var shell = files[app];
        shell.ShouldContain("<SidebarProvider>");
        shell.ShouldContain("<SidebarTrigger");
        shell.ShouldContain("<SidebarRail />");
        shell.ShouldContain("<nav aria-label=\"Modules\">");
        shell.ShouldContain("<Breadcrumbs");
        shell.ShouldContain("<CommandPalette");
        shell.ShouldContain("<Toaster />");
        shell.ShouldContain("<ThemeToggle />");
        shell.ShouldContain("<UserMenu />");
        shell.ShouldContain("moduleRoutes");
        var test = extension == "vue" ? "ts" : "tsx";
        var component = extension == "vue" ? "vue" : "tsx";
        files.Keys.ShouldContain($"web/app/src/shell/Breadcrumbs.{component}");
        files.Keys.ShouldContain($"web/app/src/shell/CommandPalette.{component}");
        files.Keys.ShouldContain($"web/app/src/shell/AppShell.test.{test}");
        files.Keys.ShouldContain($"web/app/src/shell/AppShell.axe.test.{test}");
        files.Keys.ShouldContain($"web/app/src/lib/notify.test.{test}");
        files.Keys.ShouldContain($"web/app/src/auth/UserMenu.{component}");
        files.Keys.ShouldContain($"web/app/src/auth/UserMenu.test.{test}");
        var package = files["web/app/package.json"];
        if (extension == "vue")
        {
            files.Keys.ShouldContain("web/app/src/components/ui/sidebar/Sidebar.vue");
            files.Keys.ShouldContain("web/app/src/components/ui/command/CommandDialog.vue");
            files.Keys.ShouldContain("web/app/src/components/ui/breadcrumb/Breadcrumb.vue");
            files.Keys.ShouldContain("web/app/src/components/ui/sonner/Sonner.vue");
            package.ShouldContain("\"vue-sonner\": \"2.0.9\"");
            package.ShouldContain("\"@vueuse/core\": \"15.0.0\"");
        }
        else
        {
            files.Keys.ShouldContain("web/app/src/components/ui/sidebar.tsx");
            files.Keys.ShouldContain("web/app/src/components/ui/command.tsx");
            files.Keys.ShouldContain("web/app/src/components/ui/breadcrumb.tsx");
            files.Keys.ShouldContain("web/app/src/components/ui/sonner.tsx");
            package.ShouldContain("\"cmdk\": \"1.1.1\"");
            package.ShouldContain("\"sonner\": \"2.0.8\"");
            package.ShouldContain("\"@radix-ui/react-dialog\": \"1.3.1\"");
        }
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public void The_user_menu_names_the_user_as_me_answers_and_the_sign_in_draws_no_bar_of_its_own(string profile, string[] overrides, string call)
    {
        var files = Files(profile, overrides);

        files["web/app/src/api/useCurrentUser.ts"].ShouldContain(call);
        var signedIn = files.Single(file => file.Key.StartsWith("web/app/src/auth/SignedIn.", StringComparison.Ordinal) && !file.Key.Contains(".test.", StringComparison.Ordinal)).Value;
        signedIn.ShouldNotContain("Signed in as");
        signedIn.ShouldNotContain("signoutRedirect()");
    }

    /// <summary>Out of scope (the PR says why): these keep the navigation bar they had, and get none of the shell's files.</summary>
    [Theory]
    [InlineData(React, "frontend.routing=tanstack-router")]
    [InlineData("industrial-event-driven-mfe")]
    [InlineData(React, "frontend.ui-library=radix-headless")]
    [InlineData(Vue, "frontend.ui-library=reka-ui")]
    public void Other_routers_ui_libraries_and_the_micro_frontend_shell_keep_their_navigation_bar(string profile, params string[] overrides)
    {
        var files = Files(profile, overrides);

        files.Where(file => file.Key.StartsWith("web/app/src/App.", StringComparison.Ordinal)).ShouldAllBe(file => !file.Value.Contains("Sidebar"));
        files.Keys.ShouldNotContain(path => path.StartsWith("web/app/src/shell/", StringComparison.Ordinal));
        files.Keys.ShouldNotContain("web/app/src/api/useCurrentUser.ts");
        files.Keys.ShouldNotContain(path => path.StartsWith("web/app/src/auth/UserMenu.", StringComparison.Ordinal));
    }

    [Fact]
    public void With_msal_the_shell_has_no_user_menu_and_the_sign_in_keeps_its_bar()
    {
        var files = Files(React, "backend.auth=entra-id", "frontend.auth-client=msal");

        files["web/app/src/App.tsx"].ShouldContain("<SidebarProvider>");
        files["web/app/src/App.tsx"].ShouldNotContain("UserMenu");
        files["web/app/src/auth/SignedIn.tsx"].ShouldContain("Signed in as");
    }

    /// <summary>
    /// The web app builds, lints and formats with the shell, whichever client asks /me, and its tests check the sidebar,
    /// the breadcrumbs, the command palette by keyboard alone, the toasts' roles, the user menu and the shell with axe.
    /// </summary>
    [Theory]
    [MemberData(nameof(Builds))]
    public void The_shell_builds_and_its_tests_pass(string profile, string[] overrides)
    {
        var vue = profile == Vue;
        var test = vue ? "ts" : "tsx";
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
        string[] passed =
        [
            $"src/shell/AppShell.test.{test} > the sidebar lists the start page and every module from its routes",
            $"src/shell/AppShell.test.{test} > the sidebar collapses from its trigger and opens again",
            $"src/shell/AppShell.test.{test} > on the start page the breadcrumbs name only it",
            $"src/shell/AppShell.test.{test} > the breadcrumbs lead from the start page to a module's page and below it",
            $"src/shell/AppShell.test.{test} > the page the user is on is text in the breadcrumbs, not a second link to it",
            $"src/shell/AppShell.test.{test} > Ctrl+K opens the command palette and Escape closes it",
            $"src/shell/AppShell.test.{test} > Cmd+K opens the command palette and Escape closes it",
            $"src/shell/AppShell.test.{test} > the keyboard alone goes to a page from the command palette",
            $"src/shell/AppShell.test.{test} > typing narrows the command palette to the pages that match",
            $"src/shell/AppShell.axe.test.{test} > the app shell has no accessibility violations axe can find in the light theme",
            $"src/shell/AppShell.axe.test.{test} > the app shell has no accessibility violations axe can find in the dark theme",
            $"src/shell/AppShell.axe.test.{test} > the open command palette has no accessibility violations axe can find in the dark theme",
            $"src/shell/AppShell.axe.test.{test} > the collapsed sidebar leaves no accessibility violations axe can find",
            $"src/lib/notify.test.{test} > a mutation's success shows as a status message",
            $"src/lib/notify.test.{test} > a mutation's failure shows as an alert",
            $"src/auth/UserMenu.test.{test} > the user menu names the signed-in user as /me does",
            $"src/auth/UserMenu.test.{test} > Sign out signs the user out at the identity provider",
        ];
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
