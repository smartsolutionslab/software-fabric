using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #180: frontend.ui-library=shadcn ships what components.json promises the shadcn CLI: the theme for its base colour
/// (semantic variables mapped into Tailwind, tw-animate-css) and the registry's new-york-v4 button, so components the
/// CLI adds find the utilities and the button API they use.
/// </summary>
public sealed partial class ShadcnComponentsTests
{
    private const string Profile = "saas-standard-react";

    /// <summary>A card as shadcn's CLI writes it: it styles with the theme's variables.</summary>
    private const string CliCard = """
        import type { ComponentProps } from 'react';
        import { cn } from '@/lib/utils';

        export function Card({ className, ...props }: ComponentProps<'div'>) {
          return (
            <div
              data-slot="card"
              className={cn('flex flex-col gap-6 rounded-xl border bg-card py-6 text-card-foreground shadow-sm', className)}
              {...props}
            />
          );
        }
        """;

    /// <summary>An overlay with the classes shadcn's dialog gives its own: it animates with tw-animate-css.</summary>
    private const string CliOverlay = """
        import type { ComponentProps } from 'react';
        import { cn } from '@/lib/utils';

        export function Overlay({ className, ...props }: ComponentProps<'div'>) {
          return (
            <div
              data-slot="dialog-overlay"
              className={cn(
                'fixed inset-0 z-50 bg-black/50 data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:animate-in data-[state=open]:fade-in-0',
                className,
              )}
              {...props}
            />
          );
        }
        """;

    /// <summary>A component in the registry's shape that builds on Button, as sidebar's trigger does: ghost, icon-sized.</summary>
    private const string CliSidebarTrigger = """
        import { PanelLeft } from 'lucide-react';
        import type { ComponentProps } from 'react';
        import { Button } from '@/components/ui/button';
        import { cn } from '@/lib/utils';

        export function SidebarTrigger({ className, ...props }: ComponentProps<typeof Button>) {
          return (
            <Button
              data-sidebar="trigger"
              data-slot="sidebar-trigger"
              variant="ghost"
              size="icon"
              className={cn('size-7', className)}
              {...props}
            >
              <PanelLeft />
              <span className="sr-only">Toggle Sidebar</span>
            </Button>
          );
        }
        """;

    [Fact]
    public void Shadcn_generates_its_theme_tw_animate_css_and_the_registry_button()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, []);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content);

        files.Keys.ShouldContain("web/app/src/components/ui/button.tsx");
        files.Keys.ShouldContain("web/app/src/components/ui/button.test.tsx");
        var button = files["web/app/src/components/ui/button.tsx"];
        button.ShouldContain("default: 'bg-primary text-primary-foreground");
        button.ShouldContain("export { Button, buttonVariants };");
        button.ShouldNotContain("slate");
        foreach (var variant in new[] { "destructive:", "outline:", "secondary:", "ghost:", "link:", "sm:", "lg:", "icon:" })
        {
            button.ShouldContain(variant);
        }

        files["web/app/package.json"].ShouldContain("\"tw-animate-css\": \"1.4.0\"");
        var styles = files["web/app/src/index.css"];
        styles.ShouldContain("@import 'tailwindcss';");
        styles.ShouldContain("@import 'tw-animate-css';");
        styles.ShouldContain("--card: oklch(1 0 0);");
        styles.ShouldContain("@theme inline {");
        styles.ShouldContain("--color-card: var(--card);");

        var agents = files["AGENTS.md"];
        agents.ShouldContain("## UI components: shadcn/ui");
        agents.ShouldContain("change colours and radius in `src/index.css`'s `:root`");
    }

    [Fact]
    public void Components_the_cli_adds_build_type_check_against_the_button_and_find_the_theme()
    {
        using var project = new GeneratedProject(Profile);
        project.Write("web/app/src/components/ui/card.tsx", CliCard);
        project.Write("web/app/src/components/ui/overlay.tsx", CliOverlay);
        project.Write("web/app/src/components/ui/sidebar-trigger.tsx", CliSidebarTrigger);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");

        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/components/ui/button.test.tsx > a button is a button in the theme's primary colours", customMessage: testOutput);
        plain.ShouldContain("✓ src/components/ui/button.test.tsx > with asChild the child takes the button's look and stays what it is", customMessage: testOutput);
        plain.ShouldContain("✓ src/components/ui/button.test.tsx > the destructive variant takes the theme from bg-destructive", customMessage: testOutput);
        plain.ShouldContain("✓ src/components/ui/button.test.tsx > the link variant takes the theme from text-primary", customMessage: testOutput);
        plain.ShouldContain("✓ src/components/ui/button.test.tsx > the icon size is size-9", customMessage: testOutput);

        // Tailwind compiled the utilities the components use into the stylesheet the build ships.
        var stylesheets = Directory.GetFiles(Path.Combine(app, "dist", "assets"), "*.css");
        stylesheets.ShouldNotBeEmpty();
        var css = string.Concat(stylesheets.Select(File.ReadAllText));
        css.ShouldContain(".bg-primary{");
        css.ShouldContain(".bg-card{");
        css.ShouldContain(".text-card-foreground{");
        css.ShouldContain("--card:");
        css.ShouldContain("--card-foreground:");
        css.ShouldContain("@keyframes enter");
        css.ShouldContain("animate-in");
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}
