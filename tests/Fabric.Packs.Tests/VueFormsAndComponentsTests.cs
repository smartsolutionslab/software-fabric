using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// #160: saas-standard-vue's last packs. Forms are vee-validate 4.15.1 with the pack's own zod-4 <c>toTypedSchema</c>;
/// components are shadcn-vue (#177): Reka UI's primitives styled with Tailwind, in shadcn-vue's layout, as
/// saas-standard-react uses shadcn. Reka UI alone stays the headless option, as radix-headless is React's. With them the
/// profile is generated from the real packs alone.
/// </summary>
public sealed partial class VueFormsAndComponentsTests
{
    private const string Profile = "saas-standard-vue";
    private const string RekaUi = "frontend.ui-library=reka-ui";

    /// <summary>A form a developer would write: a zod schema validates it, Tailwind styles it, the button is the project's.</summary>
    private const string SignUpForm = """
        <script setup lang="ts">
        import { useForm } from 'vee-validate';
        import { z } from 'zod';
        import { Button } from '@/components/ui/button';
        import { toTypedSchema } from '@/forms/toTypedSchema';

        const signUpSchema = z.object({
          name: z.string().trim().min(1, 'Enter a name'),
          email: z.email('Enter an email address'),
        });

        const emit = defineEmits<{ submitted: [values: z.infer<typeof signUpSchema>] }>();

        const { defineField, errors, handleSubmit } = useForm({
          validationSchema: toTypedSchema(signUpSchema),
          initialValues: { name: '', email: '' },
        });
        const [name, nameAttributes] = defineField('name');
        const [email, emailAttributes] = defineField('email');

        const submit = handleSubmit((values) => emit('submitted', values));
        </script>

        <template>
          <form class="flex flex-col gap-2 p-4" @submit="submit">
            <label for="name">Name</label>
            <input id="name" v-model="name" v-bind="nameAttributes" class="rounded border px-2" />
            <p v-if="errors.name" role="alert">{{ errors.name }}</p>
            <label for="email">Email</label>
            <input id="email" v-model="email" v-bind="emailAttributes" class="rounded border px-2" />
            <p v-if="errors.email" role="alert">{{ errors.email }}</p>
            <Button type="submit">Sign up</Button>
          </form>
        </template>
        """;

    private const string SignUpFormTest = """
        import { render, screen, waitFor } from '@testing-library/vue';
        import userEvent from '@testing-library/user-event';
        import { expect, test } from 'vitest';
        import { axe } from 'vitest-axe';
        import SignUpForm from './SignUpForm.vue';

        test('an empty sign-up shows the schema messages next to its fields', async () => {
          render(SignUpForm);

          await userEvent.click(screen.getByRole('button', { name: 'Sign up' }));

          expect(await screen.findByText('Enter a name')).toHaveAttribute('role', 'alert');
          expect(await screen.findByText('Enter an email address')).toHaveAttribute('role', 'alert');
        });

        test('a valid sign-up submits the values the schema puts out', async () => {
          const { emitted } = render(SignUpForm);

          await userEvent.type(screen.getByLabelText('Name'), '  Ada  ');
          await userEvent.type(screen.getByLabelText('Email'), 'ada@example.com');
          await userEvent.click(screen.getByRole('button', { name: 'Sign up' }));

          await waitFor(() => expect(emitted('submitted')).toEqual([[{ name: 'Ada', email: 'ada@example.com' }]]));
          expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        });

        test('the sign-up form has no accessibility violations axe can find', async () => {
          const { container } = render(SignUpForm);

          expect(await axe(container)).toHaveNoViolations();
        });
        """;

    [Fact]
    public void The_vue_profile_is_generated_from_the_real_packs_alone_with_forms_components_and_tailwind()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, []);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content);

        files.Keys.ShouldContain("web/app/src/forms/toTypedSchema.ts");
        files.Keys.ShouldContain("web/app/src/forms/toTypedSchema.test.ts");
        files.Keys.ShouldContain("web/app/components.json");
        files.Keys.ShouldContain("web/app/src/components/ui/button/Button.vue");
        files.Keys.ShouldContain("web/app/src/components/ui/button/index.ts");
        files.Keys.ShouldContain("web/app/src/components/ui/button/Button.test.ts");
        files.Keys.ShouldContain("web/app/src/lib/utils.ts");
        files.Keys.ShouldNotContain("web/app/src/components/ui/BaseButton.vue");
        files.Keys.Where(path => path.EndsWith(".tsx", StringComparison.Ordinal)).ShouldBeEmpty();

        var components = files["web/app/components.json"];
        components.ShouldContain("\"$schema\": \"https://shadcn-vue.com/schema.json\"");
        components.ShouldContain("\"typescript\": true");
        components.ShouldContain("\"css\": \"src/index.css\"");
        components.ShouldContain("\"ui\": \"@/components/ui\"");
        components.ShouldNotContain("\"tsx\"");

        var package = files["web/app/package.json"];
        package.ShouldContain("\"vee-validate\": \"4.15.1\"");
        package.ShouldContain("\"reka-ui\": \"2.11.0\"");
        package.ShouldContain("\"@lucide/vue\": \"1.65.0\"");
        package.ShouldContain("\"tailwindcss\": \"4.3.3\"");
        package.ShouldNotContain("primevue");
        package.ShouldNotContain("@vee-validate/zod");
        package.ShouldNotContain("react");
        files["web/app/src/index.css"].ShouldContain("@import 'tailwindcss';");
        files["web/app/vite.config.ts"].ShouldContain("tailwindcss()");

        var agents = files["AGENTS.md"];
        agents.ShouldContain("## Forms: vee-validate");
        agents.ShouldContain("## UI components: shadcn-vue");
        agents.ShouldContain("pnpm dlx shadcn-vue@2.9.0 add <name>");
        agents.ShouldContain("Style with utility classes in `class`");
        agents.ShouldNotContain("className");
        agents.ShouldNotContain("shadcn@");
        agents.ShouldNotContain("## UI components: Reka UI");
    }

    [Fact]
    public void Reka_ui_stays_the_headless_option_and_generates_without_shadcn_vue()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [RekaUi]);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content);

        files.Keys.ShouldContain("web/app/src/components/ui/BaseButton.vue");
        files.Keys.ShouldContain("web/app/src/components/ui/BaseButton.test.ts");
        files.Keys.ShouldContain("web/app/src/lib/utils.ts");
        files.Keys.ShouldNotContain("web/app/components.json");
        files.Keys.ShouldNotContain("web/app/src/components/ui/button/Button.vue");
        files["web/app/package.json"].ShouldContain("\"reka-ui\": \"2.11.0\"");
        files["web/app/package.json"].ShouldNotContain("@lucide/vue");
        files["AGENTS.md"].ShouldContain("## UI components: Reka UI");
        files["AGENTS.md"].ShouldNotContain("shadcn");
    }

    [Fact]
    public void A_form_validated_by_a_zod_schema_builds_lints_shows_its_errors_submits_and_passes_axe()
    {
        using var project = new GeneratedProject(Profile);
        project.Write("web/app/src/pages/SignUpForm.vue", SignUpForm);
        project.Write("web/app/src/pages/SignUpForm.test.ts", SignUpFormTest);
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
        plain.ShouldContain("✓ src/components/ui/button/Button.test.ts > a button is a button with the default look", customMessage: testOutput);
        plain.ShouldContain("✓ src/components/ui/button/Button.test.ts > with as-child the child takes the button's look and stays what it is", customMessage: testOutput);
        plain.ShouldContain("✓ src/forms/toTypedSchema.test.ts > a schema's messages reach the fields they belong to, nested and listed ones too", customMessage: testOutput);
        plain.ShouldContain("✓ src/forms/toTypedSchema.test.ts > valid values come back as the schema puts them out", customMessage: testOutput);
        plain.ShouldContain("✓ src/pages/SignUpForm.test.ts > an empty sign-up shows the schema messages next to its fields", customMessage: testOutput);
        plain.ShouldContain("✓ src/pages/SignUpForm.test.ts > a valid sign-up submits the values the schema puts out", customMessage: testOutput);
        plain.ShouldContain("✓ src/pages/SignUpForm.test.ts > the sign-up form has no accessibility violations axe can find", customMessage: testOutput);

        // Tailwind compiled the utilities the components use into the stylesheet the build ships.
        var stylesheets = Directory.GetFiles(Path.Combine(app, "dist", "assets"), "*.css");
        stylesheets.ShouldNotBeEmpty();
        string.Concat(stylesheets.Select(File.ReadAllText)).ShouldContain(".bg-slate-900");
    }

    [Fact]
    public void Importing_another_component_or_form_library_fails_lint_in_vue()
    {
        using var project = new GeneratedProject(Profile);
        project.Write("web/app/src/pages/OtherLibraries.ts", """
            import { toTypedSchema } from '@vee-validate/zod';
            import { FormKit } from '@formkit/vue';
            import Button from 'primevue/button';
            import { createVuetify } from 'vuetify';
            import { ElButton } from 'element-plus';

            export const others = [toTypedSchema, FormKit, Button, createVuetify, ElButton];
            """);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var (installExitCode, installOutput) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{installOutput}");

        var (exitCode, output) = Shell.Run(web, "pnpm", "lint");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("'@vee-validate/zod' import is restricted from being used by a pattern. Use vee-validate with toTypedSchema from src/forms/toTypedSchema (docs/adr)");
        output.ShouldContain("'@formkit/vue' import is restricted from being used by a pattern. Use vee-validate with toTypedSchema from src/forms/toTypedSchema (docs/adr)");
        output.ShouldContain("'primevue/button' import is restricted from being used by a pattern. Use the shadcn-vue components in src/components/ui (docs/adr)");
        output.ShouldContain("'vuetify' import is restricted from being used by a pattern. Use the shadcn-vue components in src/components/ui (docs/adr)");
        output.ShouldContain("'element-plus' import is restricted from being used by a pattern. Use the shadcn-vue components in src/components/ui (docs/adr)");
    }

    [Fact]
    public void Reka_ui_on_its_own_builds_lints_passes_its_tests_and_refuses_other_component_libraries()
    {
        using var project = new GeneratedProject(Profile, RekaUi);
        var web = Path.Combine(project.ProjectDirectory, "web");
        var app = Path.Combine(web, "app");
        foreach (var step in new[] { "install", "build" })
        {
            var (stepExitCode, stepOutput) = Shell.Run(web, "pnpm", step);
            stepExitCode.ShouldBe(0, $"pnpm {step}:\n{stepOutput}");
        }

        var (testExitCode, testOutput) = Shell.Run(app, "pnpm", "test", "--reporter=verbose");
        project.Write("web/app/src/pages/OtherLibraries.ts", """
            import Button from 'primevue/button';
            import { createVuetify } from 'vuetify';

            export const others = [Button, createVuetify];
            """);
        var (lintExitCode, lintOutput) = Shell.Run(web, "pnpm", "lint");

        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/components/ui/BaseButton.test.ts > a button is a button with the default look", customMessage: testOutput);
        plain.ShouldContain("✓ src/components/ui/BaseButton.test.ts > with as-child the child takes the button's look and stays what it is", customMessage: testOutput);
        lintExitCode.ShouldNotBe(0, lintOutput);
        lintOutput.ShouldContain("'primevue/button' import is restricted from being used by a pattern. Use Reka UI and the components in src/components/ui (docs/adr)");
        lintOutput.ShouldContain("'vuetify' import is restricted from being used by a pattern. Use Reka UI and the components in src/components/ui (docs/adr)");
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}
