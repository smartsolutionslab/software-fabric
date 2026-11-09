using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.forms.tanstack-form (#268): TanStack Form on React and on Vue, validating with the project's zod or Valibot
/// schema through Standard Schema, and the TanStack Form branches of the agent panel and both dashboards packs.
/// </summary>
public sealed partial class TanStackFormTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";
    private const string TanStackForm = "frontend.forms=tanstack-form";
    private const string Valibot = "frontend.validation=valibot";
    private const string OpenAI = "backend.llm=openai";
    private const string AgentFramework = "backend.llm.agents=agent-framework";
    private const string AgUi = "frontend.agent-ui=ag-ui";
    private const string Dashboards = "frontend.dashboards=user-editable,generative";

    /// <summary>The web files of the agent panel and the dashboards that hold a form, per framework.</summary>
    private static readonly string[] ReactForms =
    [
        "web/app/src/agents/AgentPanel.tsx",
        "web/app/src/dashboards/DashboardCanvas.tsx",
        "web/app/src/dashboards/DashboardAssistant.tsx",
    ];

    private static readonly string[] VueForms =
    [
        "web/app/src/agents/AgentConversation.vue",
        "web/app/src/dashboards/DashboardCanvas.vue",
        "web/app/src/dashboards/DashboardAssistant.vue",
    ];

    private static readonly string[] VueAgentAndDashboards = [OpenAI, AgentFramework, AgUi, Dashboards];

    /// <summary>A React form a developer would write with TanStack Form and the project's zod schema.</summary>
    private const string ReactSignUpForm = """
        import { revalidateLogic, useForm } from '@tanstack/react-form';
        import { z } from 'zod';
        import { Button } from '@/components/ui/button';

        const signUpSchema = z.object({
          name: z.string().trim().min(1, 'Enter a name'),
          email: z.email('Enter an email address'),
        });

        type SignUp = z.infer<typeof signUpSchema>;

        export function SignUpForm({ onSubmitted }: { onSubmitted: (values: SignUp) => void }) {
          const form = useForm({
            defaultValues: { name: '', email: '' },
            validationLogic: revalidateLogic(),
            validators: { onDynamic: signUpSchema },
            onSubmit: ({ value }) => onSubmitted(signUpSchema.parse(value)),
          });

          return (
            <form
              onSubmit={(event) => {
                event.preventDefault();
                void form.handleSubmit();
              }}
            >
              <form.Field name="name">
                {(field) => (
                  <>
                    <label htmlFor={field.name}>Name</label>
                    <input
                      id={field.name}
                      name={field.name}
                      value={field.state.value}
                      onBlur={field.handleBlur}
                      onChange={(event) => field.handleChange(event.target.value)}
                    />
                    {field.state.meta.errors.length > 0 && <p role="alert">{field.state.meta.errors[0]?.message}</p>}
                  </>
                )}
              </form.Field>
              <form.Field name="email">
                {(field) => (
                  <>
                    <label htmlFor={field.name}>Email</label>
                    <input
                      id={field.name}
                      name={field.name}
                      value={field.state.value}
                      onBlur={field.handleBlur}
                      onChange={(event) => field.handleChange(event.target.value)}
                    />
                    {field.state.meta.errors.length > 0 && <p role="alert">{field.state.meta.errors[0]?.message}</p>}
                  </>
                )}
              </form.Field>
              <Button type="submit">Sign up</Button>
            </form>
          );
        }
        """;

    private const string ReactSignUpFormTest = """
        import { cleanup, render, screen, waitFor } from '@testing-library/react';
        import userEvent from '@testing-library/user-event';
        import { afterEach, expect, test, vi } from 'vitest';
        import { SignUpForm } from './SignUpForm';

        // Vitest runs without globals, so Testing Library cannot register its own cleanup.
        afterEach(cleanup);

        test('an empty TanStack Form sign-up shows the schema messages next to its fields', async () => {
          render(<SignUpForm onSubmitted={vi.fn()} />);

          await userEvent.click(screen.getByRole('button', { name: 'Sign up' }));

          expect(await screen.findByText('Enter a name')).toHaveAttribute('role', 'alert');
          expect(await screen.findByText('Enter an email address')).toHaveAttribute('role', 'alert');
        });

        test('a valid TanStack Form sign-up submits the values the schema puts out', async () => {
          const submitted = vi.fn();
          render(<SignUpForm onSubmitted={submitted} />);

          await userEvent.type(screen.getByLabelText('Name'), '  Ada  ');
          await userEvent.type(screen.getByLabelText('Email'), 'ada@example.com');
          await userEvent.click(screen.getByRole('button', { name: 'Sign up' }));

          await waitFor(() => expect(submitted).toHaveBeenCalledWith({ name: 'Ada', email: 'ada@example.com' }));
          expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        });
        """;

    /// <summary>A Vue form a developer would write with TanStack Form and the project's zod schema.</summary>
    private const string VueSignUpForm = """
        <script setup lang="ts">
        import { revalidateLogic, useForm } from '@tanstack/vue-form';
        import { z } from 'zod';
        import { Button } from '@/components/ui/button';

        const signUpSchema = z.object({
          name: z.string().trim().min(1, 'Enter a name'),
          email: z.email('Enter an email address'),
        });

        const emit = defineEmits<{ submitted: [values: z.infer<typeof signUpSchema>] }>();

        const form = useForm({
          defaultValues: { name: '', email: '' },
          validationLogic: revalidateLogic(),
          validators: { onDynamic: signUpSchema },
          onSubmit: ({ value }) => emit('submitted', signUpSchema.parse(value)),
        });

        function submit(event: Event) {
          event.preventDefault();
          void form.handleSubmit();
        }
        </script>

        <template>
          <form class="flex flex-col gap-2 p-4" @submit="submit">
            <form.Field v-slot="{ field }" name="name">
              <label :for="field.name">Name</label>
              <input
                :id="field.name"
                :name="field.name"
                :value="field.state.value"
                class="rounded border px-2"
                @blur="field.handleBlur"
                @input="field.handleChange(($event.target as HTMLInputElement).value)"
              />
              <p v-if="field.state.meta.errors.length > 0" role="alert">{{ field.state.meta.errors[0]?.message }}</p>
            </form.Field>
            <form.Field v-slot="{ field }" name="email">
              <label :for="field.name">Email</label>
              <input
                :id="field.name"
                :name="field.name"
                :value="field.state.value"
                class="rounded border px-2"
                @blur="field.handleBlur"
                @input="field.handleChange(($event.target as HTMLInputElement).value)"
              />
              <p v-if="field.state.meta.errors.length > 0" role="alert">{{ field.state.meta.errors[0]?.message }}</p>
            </form.Field>
            <Button type="submit">Sign up</Button>
          </form>
        </template>
        """;

    private const string VueSignUpFormTest = """
        import { render, screen, waitFor } from '@testing-library/vue';
        import userEvent from '@testing-library/user-event';
        import { expect, test } from 'vitest';
        import SignUpForm from './SignUpForm.vue';

        test('an empty TanStack Form sign-up shows the schema messages next to its fields', async () => {
          render(SignUpForm);

          await userEvent.click(screen.getByRole('button', { name: 'Sign up' }));

          expect(await screen.findByText('Enter a name')).toHaveAttribute('role', 'alert');
          expect(await screen.findByText('Enter an email address')).toHaveAttribute('role', 'alert');
        });

        test('a valid TanStack Form sign-up submits the values the schema puts out', async () => {
          const { emitted } = render(SignUpForm);

          await userEvent.type(screen.getByLabelText('Name'), '  Ada  ');
          await userEvent.type(screen.getByLabelText('Email'), 'ada@example.com');
          await userEvent.click(screen.getByRole('button', { name: 'Sign up' }));

          await waitFor(() => expect(emitted('submitted')).toEqual([[{ name: 'Ada', email: 'ada@example.com' }]]));
          expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        });
        """;

    private const string OtherReactFormLibraries = """
        import { zodResolver } from '@hookform/resolvers/zod';
        import { useFormik } from 'formik';
        import { useForm } from 'react-hook-form';

        export const others = [zodResolver, useFormik, useForm];
        """;

    private const string OtherVueFormLibraries = """
        import { plugin } from '@formkit/vue';
        import { toTypedSchema } from '@vee-validate/zod';
        import { useForm } from 'vee-validate';

        export const others = [plugin, toTypedSchema, useForm];
        """;

    [Fact]
    public void TanStack_Form_replaces_react_hook_form_in_the_dependencies_the_rules_the_adr_and_the_lint_rule_on_react()
    {
        var files = Files(React, TanStackForm);

        files["web/app/package.json"].ShouldContain("\"@tanstack/react-form\": \"1.33.5\"");
        files["web/app/package.json"].ShouldNotContain("react-hook-form");
        files["web/app/package.json"].ShouldNotContain("@hookform/");
        files["web/app/eslint.config.js"].ShouldContain("group: ['react-hook-form', '@hookform/*', 'formik', 'react-final-form', 'final-form'],");
        files["web/app/eslint.config.js"].ShouldContain("message: 'Use TanStack Form (docs/adr).'");
        files["web/app/eslint.config.js"].ShouldNotContain("Use react-hook-form");
        files["AGENTS.md"].ShouldContain("## Forms: TanStack Form");
        files["AGENTS.md"].ShouldContain("`useForm({ defaultValues, validationLogic: revalidateLogic(), validators: { onDynamic: schema }, onSubmit })` from `@tanstack/react-form`");
        files["AGENTS.md"].ShouldContain("`schema.parse(value)`");
        files["AGENTS.md"].ShouldNotContain("## Forms: React Hook Form");
        Adr(files).ShouldContain("TanStack Form");
        files.Keys.ShouldNotContain(path => path.EndsWith("-frontend-forms-react-hook-form.md", StringComparison.Ordinal));
    }

    [Fact]
    public void TanStack_Form_replaces_vee_validate_and_its_adapter_in_the_dependencies_the_rules_the_adr_and_the_lint_rule_on_vue()
    {
        var files = Files(Vue, TanStackForm);

        files["web/app/package.json"].ShouldContain("\"@tanstack/vue-form\": \"1.33.5\"");
        files["web/app/package.json"].ShouldNotContain("vee-validate");
        files.Keys.ShouldNotContain(path => path.StartsWith("web/app/src/forms/", StringComparison.Ordinal));
        files["web/app/eslint.config.js"].ShouldContain("group: ['vee-validate', '@vee-validate/*', '@formkit/*'],");
        files["web/app/eslint.config.js"].ShouldContain("message: 'Use TanStack Form (docs/adr).'");
        files["web/app/eslint.config.js"].ShouldNotContain("Use vee-validate");
        files["AGENTS.md"].ShouldContain("## Forms: TanStack Form");
        files["AGENTS.md"].ShouldContain("from `@tanstack/vue-form`");
        files["AGENTS.md"].ShouldContain("`<form.Field v-slot=\"{ field }\" name=\"...\">`");
        files["AGENTS.md"].ShouldNotContain("## Forms: vee-validate");
        files["AGENTS.md"].ShouldNotContain("toTypedSchema");
        Adr(files).ShouldContain("TanStack Form");
    }

    [Fact]
    public void With_valibot_the_rules_take_the_schemas_output_with_valibots_parse_and_name_no_zod()
    {
        foreach (var profile in new[] { React, Vue })
        {
            var files = Files(profile, TanStackForm, Valibot);

            files["AGENTS.md"].ShouldContain("`v.parse(schema, value)`", customMessage: profile);
            files["AGENTS.md"].ShouldNotContain("schema.parse(value)`", customMessage: profile);
            Adr(files).ShouldNotContain("zod", Case.Insensitive, profile);
        }
    }

    [Theory]
    [InlineData("zod", "z.object(")]
    [InlineData("valibot", "v.object(")]
    public void The_react_agent_panel_and_dashboards_hold_tanstack_forms_and_name_no_other_form_library(string validation, string schema)
    {
        var files = Files("saas-ai-react", TanStackForm, $"frontend.validation={validation}");

        foreach (var path in ReactForms)
        {
            files[path].ShouldContain("import { revalidateLogic, useForm", customMessage: path);
            files[path].ShouldContain("from '@tanstack/react-form';", customMessage: path);
            files[path].ShouldContain("validationLogic: revalidateLogic(),", customMessage: path);
            files[path].ShouldContain(schema, customMessage: path);
            files[path].ShouldNotContain("throw new Error(", customMessage: path);
        }

        files["web/app/src/agents/AgentPanel.tsx"].ShouldContain("validators: { onDynamic: messageSchema },");
        files["web/app/src/dashboards/DashboardCanvas.tsx"].ShouldContain("validators: { onDynamic: nameSchema },");
        files["web/app/src/dashboards/DashboardCanvas.tsx"].ShouldContain("validators: { onDynamic: widgetSchema },");
        files["web/app/src/dashboards/DashboardAssistant.tsx"].ShouldContain("validators: { onDynamic: questionSchema },");
        WebSources(files).ShouldNotContain(source => source.Value.Contains("react-hook-form", StringComparison.Ordinal) || source.Value.Contains("@hookform/", StringComparison.Ordinal), "no web source names React Hook Form");
        Adr(files, "agent-ui-ag-ui").ShouldContain("shadcn/ui components and TanStack Form");
    }

    [Theory]
    [InlineData("zod", "z.object(")]
    [InlineData("valibot", "v.object(")]
    public void The_vue_agent_panel_and_dashboards_hold_tanstack_forms_and_name_no_other_form_library(string validation, string schema)
    {
        var files = Files(Vue, [TanStackForm, $"frontend.validation={validation}", .. VueAgentAndDashboards]);

        foreach (var path in VueForms)
        {
            files[path].ShouldContain("import { revalidateLogic, useForm } from '@tanstack/vue-form';", customMessage: path);
            files[path].ShouldContain("validationLogic: revalidateLogic(),", customMessage: path);
            files[path].ShouldContain(".Field v-slot=\"{ field", customMessage: path);
            files[path].ShouldContain(schema, customMessage: path);
            files[path].ShouldNotContain("throw new Error(", customMessage: path);
        }

        files["web/app/src/agents/AgentConversation.vue"].ShouldContain("validators: { onDynamic: messageSchema },");
        files["web/app/src/dashboards/DashboardCanvas.vue"].ShouldContain("validators: { onDynamic: nameSchema },");
        files["web/app/src/dashboards/DashboardCanvas.vue"].ShouldContain("validators: { onDynamic: widgetSchema },");
        files["web/app/src/dashboards/DashboardAssistant.vue"].ShouldContain("validators: { onDynamic: questionSchema },");
        WebSources(files).ShouldNotContain(source => source.Value.Contains("vee-validate", StringComparison.Ordinal) || source.Value.Contains("toTypedSchema", StringComparison.Ordinal), "no web source names vee-validate or its adapter");
        Adr(files, "agent-ui-ag-ui").ShouldContain("shadcn-vue components and TanStack Form");
        Adr(files, "dashboards-user-editable").ShouldContain("with TanStack Form and");
        files["AGENTS.md"].ShouldContain("and the TanStack Form forms)");
    }

    [Fact]
    public void With_another_vue_ui_library_the_tanstack_form_screens_fail_saying_what_they_are_built_from()
    {
        var files = Files(Vue, [TanStackForm, "frontend.ui-library=reka-ui", .. VueAgentAndDashboards]);

        files["web/app/src/agents/AgentConversation.vue"].ShouldStartWith("<script lang=\"ts\">\nthrow new Error('The ag-ui pack builds the Vue agent panel from shadcn-vue with vee-validate, TanStack Form or FormKit, or reka-ui with vee-validate, and zod or valibot only so far; frontend.ui-library is reka-ui, frontend.forms is tanstack-form");
        files["web/app/src/dashboards/DashboardCanvas.vue"].ShouldStartWith("<script lang=\"ts\">\nthrow new Error('The dashboards pack builds the Vue dashboard page from shadcn-vue with vee-validate, TanStack Form or FormKit, or reka-ui with vee-validate, and zod or valibot only so far; frontend.ui-library is reka-ui, frontend.forms is tanstack-form");
        files["web/app/src/dashboards/DashboardAssistant.vue"].ShouldStartWith("<script lang=\"ts\">\nthrow new Error('The generative dashboards pack builds the Vue assistant from shadcn-vue with vee-validate, TanStack Form or FormKit, or reka-ui with vee-validate, and zod or valibot only so far; frontend.ui-library is reka-ui, frontend.forms is tanstack-form");
    }

    [Fact]
    public void A_react_tanstack_form_builds_lints_shows_its_errors_submits_and_lint_refuses_the_other_form_libraries()
    {
        using var project = new GeneratedProject(React, TanStackForm);
        project.Write("web/app/src/pages/SignUpForm.tsx", ReactSignUpForm);
        project.Write("web/app/src/pages/SignUpForm.test.tsx", ReactSignUpFormTest);

        var testOutput = Plain(InstallBuildLintAndTest(project));

        testOutput.ShouldContain("✓ src/pages/SignUpForm.test.tsx > an empty TanStack Form sign-up shows the schema messages next to its fields");
        testOutput.ShouldContain("✓ src/pages/SignUpForm.test.tsx > a valid TanStack Form sign-up submits the values the schema puts out");
        var lintOutput = LintRefuses(project, OtherReactFormLibraries);
        lintOutput.ShouldContain("'@hookform/resolvers/zod' import is restricted from being used by a pattern. Use TanStack Form (docs/adr)");
        lintOutput.ShouldContain("'formik' import is restricted from being used by a pattern. Use TanStack Form (docs/adr)");
        lintOutput.ShouldContain("'react-hook-form' import is restricted from being used by a pattern. Use TanStack Form (docs/adr)");
    }

    [Fact]
    public void A_vue_tanstack_form_builds_lints_shows_its_errors_submits_and_lint_refuses_the_other_form_libraries()
    {
        using var project = new GeneratedProject(Vue, TanStackForm);
        project.Write("web/app/src/pages/SignUpForm.vue", VueSignUpForm);
        project.Write("web/app/src/pages/SignUpForm.test.ts", VueSignUpFormTest);

        var testOutput = Plain(InstallBuildLintAndTest(project));

        testOutput.ShouldContain("✓ src/pages/SignUpForm.test.ts > an empty TanStack Form sign-up shows the schema messages next to its fields");
        testOutput.ShouldContain("✓ src/pages/SignUpForm.test.ts > a valid TanStack Form sign-up submits the values the schema puts out");
        var lintOutput = LintRefuses(project, OtherVueFormLibraries);
        lintOutput.ShouldContain("'@formkit/vue' import is restricted from being used by a pattern. Use TanStack Form (docs/adr)");
        lintOutput.ShouldContain("'@vee-validate/zod' import is restricted from being used by a pattern. Use TanStack Form (docs/adr)");
        lintOutput.ShouldContain("'vee-validate' import is restricted from being used by a pattern. Use TanStack Form (docs/adr)");
    }

    [Fact]
    public void The_react_agent_panel_and_dashboards_on_tanstack_form_build_lint_pass_their_own_tests_and_stay_formatted()
    {
        using var project = new GeneratedProject("saas-ai-react", TanStackForm);

        var testOutput = Plain(InstallBuildLintAndTest(project));

        testOutput.ShouldContain("✓ src/agents/AgentPanel.test.tsx > ");
        testOutput.ShouldContain("✓ src/dashboards/DashboardEditor.test.tsx > a new dashboard gets a fresh time-ordered id and needs a name before it is saved");
        testOutput.ShouldContain("✓ src/dashboards/DashboardEditor.test.tsx > paging the widget picker clears the choice, so a widget of one page is never added as a visual of another");
        testOutput.ShouldContain("✓ src/dashboards/DashboardAssistant.test.tsx > ");
        FormatCheck(project);
    }

    [Fact]
    public void The_vue_agent_panel_and_dashboards_on_tanstack_form_build_lint_pass_their_own_tests_and_stay_formatted()
    {
        using var project = new GeneratedProject(Vue, [TanStackForm, .. VueAgentAndDashboards]);

        var testOutput = Plain(InstallBuildLintAndTest(project));

        testOutput.ShouldContain("✓ src/agents/AgentPanel.test.ts > ");
        testOutput.ShouldContain("✓ src/dashboards/DashboardEditor.test.ts > a new dashboard gets a fresh time-ordered id and needs a name before it is saved");
        testOutput.ShouldContain("✓ src/dashboards/DashboardEditor.test.ts > paging the widget picker clears the choice, so a widget of one page is never added as a visual of another");
        testOutput.ShouldContain("✓ src/dashboards/DashboardAssistant.test.ts > ");
        FormatCheck(project);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty($"{profile} {string.Join(' ', overrides)}");
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>The generated ADR of an option, by the end of its file name; the forms option's by default.</summary>
    private static string Adr(Dictionary<string, string> files, string option = "forms-tanstack-form") =>
        files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Key.EndsWith($"-frontend-{option}.md", StringComparison.Ordinal)).Value;

    private static IEnumerable<KeyValuePair<string, string>> WebSources(Dictionary<string, string> files) =>
        files.Where(file => file.Key.StartsWith("web/", StringComparison.Ordinal) && Regex.IsMatch(file.Key, @"\.(ts|tsx|vue)$"));

    /// <summary>Installs, builds and lints the web workspace, runs the app's tests, and returns their verbose output.</summary>
    private static string InstallBuildLintAndTest(GeneratedProject project)
    {
        var web = Path.Combine(project.ProjectDirectory, "web");
        foreach (var step in new[] { "install", "build", "lint" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", step);
            exitCode.ShouldBe(0, $"pnpm {step}:\n{output}");
        }

        var (testExitCode, testOutput) = Shell.Run(Path.Combine(web, "app"), "pnpm", "test", "--reporter=verbose");
        testExitCode.ShouldBe(0, $"pnpm test:\n{testOutput}");
        return testOutput;
    }

    private static void FormatCheck(GeneratedProject project)
    {
        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "format:check");
        exitCode.ShouldBe(0, output);
    }

    private static string LintRefuses(GeneratedProject project, string source)
    {
        project.Write("web/app/src/pages/OtherLibraries.ts", source);
        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "lint");
        exitCode.ShouldNotBe(0, output);
        return output;
    }

    private static string Plain(string output) => Escapes().Replace(output, string.Empty);

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}
