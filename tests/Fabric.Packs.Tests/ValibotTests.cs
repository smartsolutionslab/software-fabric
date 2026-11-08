using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.validation.valibot (#267): Valibot in place of zod, with React Hook Form's <c>valibotResolver</c>,
/// vee-validate's project-owned <c>toTypedSchema</c> over Valibot, and the Valibot branches of the agent panel and both
/// dashboards packs, <c>proposal.ts</c> included (owner decision on #267).
/// </summary>
public sealed partial class ValibotTests
{
    private const string React = "saas-standard-react";
    private const string Vue = "saas-standard-vue";
    private const string Valibot = "frontend.validation=valibot";
    private const string OpenAI = "backend.llm=openai";
    private const string AgentFramework = "backend.llm.agents=agent-framework";
    private const string AgUi = "frontend.agent-ui=ag-ui";
    private const string Dashboards = "frontend.dashboards=user-editable,generative";
    private const string Adapter = "web/app/src/forms/toTypedSchema.ts";
    private const string AdapterTest = "web/app/src/forms/toTypedSchema.test.ts";

    /// <summary>The web files of the agent panel and the dashboards that validate input, per framework.</summary>
    private static readonly string[] ReactValidating =
    [
        "web/app/src/agents/AgentPanel.tsx",
        "web/app/src/dashboards/DashboardCanvas.tsx",
        "web/app/src/dashboards/DashboardAssistant.tsx",
        "web/app/src/dashboards/proposal.ts",
    ];

    private static readonly string[] VueValidating =
    [
        "web/app/src/agents/AgentConversation.vue",
        "web/app/src/dashboards/DashboardCanvas.vue",
        "web/app/src/dashboards/DashboardAssistant.vue",
        "web/app/src/dashboards/proposal.ts",
        Adapter,
        AdapterTest,
    ];

    /// <summary>A React form a developer would write with Valibot and React Hook Form.</summary>
    private const string ReactSignUpForm = """
        import { valibotResolver } from '@hookform/resolvers/valibot';
        import { useForm } from 'react-hook-form';
        import * as v from 'valibot';
        import { Button } from '@/components/ui/button';

        const signUpSchema = v.object({
          name: v.pipe(v.string(), v.trim(), v.minLength(1, 'Enter a name')),
          email: v.pipe(v.string(), v.email('Enter an email address')),
        });

        type SignUp = v.InferOutput<typeof signUpSchema>;

        export function SignUpForm({ onSubmitted }: { onSubmitted: (values: SignUp) => void }) {
          const {
            register,
            handleSubmit,
            formState: { errors },
          } = useForm<SignUp>({ resolver: valibotResolver(signUpSchema), defaultValues: { name: '', email: '' } });

          return (
            <form onSubmit={handleSubmit((values) => onSubmitted(values))}>
              <label htmlFor="name">Name</label>
              <input id="name" {...register('name')} />
              {errors.name && <p role="alert">{errors.name.message}</p>}
              <label htmlFor="email">Email</label>
              <input id="email" {...register('email')} />
              {errors.email && <p role="alert">{errors.email.message}</p>}
              <Button type="submit">Sign up</Button>
            </form>
          );
        }
        """;

    private const string ReactSignUpFormTest = """
        import { render, screen, waitFor } from '@testing-library/react';
        import userEvent from '@testing-library/user-event';
        import { expect, test, vi } from 'vitest';
        import { SignUpForm } from './SignUpForm';

        test('an empty valibot sign-up shows the schema messages next to its fields', async () => {
          render(<SignUpForm onSubmitted={vi.fn()} />);

          await userEvent.click(screen.getByRole('button', { name: 'Sign up' }));

          expect(await screen.findByText('Enter a name')).toHaveAttribute('role', 'alert');
          expect(await screen.findByText('Enter an email address')).toHaveAttribute('role', 'alert');
        });

        test('a valid valibot sign-up submits the values the schema puts out', async () => {
          const submitted = vi.fn();
          render(<SignUpForm onSubmitted={submitted} />);

          await userEvent.type(screen.getByLabelText('Name'), '  Ada  ');
          await userEvent.type(screen.getByLabelText('Email'), 'ada@example.com');
          await userEvent.click(screen.getByRole('button', { name: 'Sign up' }));

          await waitFor(() => expect(submitted).toHaveBeenCalledWith({ name: 'Ada', email: 'ada@example.com' }));
          expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        });
        """;

    /// <summary>A Vue form a developer would write with Valibot and vee-validate, through the project's adapter.</summary>
    private const string VueSignUpForm = """
        <script setup lang="ts">
        import * as v from 'valibot';
        import { useForm } from 'vee-validate';
        import { Button } from '@/components/ui/button';
        import { toTypedSchema } from '@/forms/toTypedSchema';

        const signUpSchema = v.object({
          name: v.pipe(v.string(), v.trim(), v.minLength(1, 'Enter a name')),
          email: v.pipe(v.string(), v.email('Enter an email address')),
        });

        const emit = defineEmits<{ submitted: [values: v.InferOutput<typeof signUpSchema>] }>();

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

    private const string VueSignUpFormTest = """
        import { render, screen, waitFor } from '@testing-library/vue';
        import userEvent from '@testing-library/user-event';
        import { expect, test } from 'vitest';
        import SignUpForm from './SignUpForm.vue';

        test('an empty valibot sign-up shows the schema messages next to its fields', async () => {
          render(SignUpForm);

          await userEvent.click(screen.getByRole('button', { name: 'Sign up' }));

          expect(await screen.findByText('Enter a name')).toHaveAttribute('role', 'alert');
          expect(await screen.findByText('Enter an email address')).toHaveAttribute('role', 'alert');
        });

        test('a valid valibot sign-up submits the values the schema puts out', async () => {
          const { emitted } = render(SignUpForm);

          await userEvent.type(screen.getByLabelText('Name'), '  Ada  ');
          await userEvent.type(screen.getByLabelText('Email'), 'ada@example.com');
          await userEvent.click(screen.getByRole('button', { name: 'Sign up' }));

          await waitFor(() => expect(emitted('submitted')).toEqual([[{ name: 'Ada', email: 'ada@example.com' }]]));
          expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        });
        """;

    private const string OtherSchemaLibraries = """
        import { z } from 'zod';
        import * as yup from 'yup';

        export const others = [z, yup];
        """;

    [Fact]
    public void Valibot_replaces_zod_in_the_dependencies_the_rules_the_adr_and_the_lint_rule_on_react_and_vue()
    {
        foreach (var profile in new[] { React, Vue })
        {
            var files = Files(profile, Valibot);

            files["web/app/package.json"].ShouldContain("\"valibot\": \"1.5.0\"", customMessage: profile);
            files["web/app/package.json"].ShouldNotContain("\"zod\"", customMessage: profile);
            files["web/app/eslint.config.js"].ShouldContain("{ group: ['yup', 'joi', 'zod'], message: 'Use valibot (docs/adr).' }", customMessage: profile);
            files["web/app/eslint.config.js"].ShouldNotContain("Use zod", customMessage: profile);
            files["AGENTS.md"].ShouldContain("## Validation: Valibot", customMessage: profile);
            files["AGENTS.md"].ShouldContain("type Invoice = v.InferOutput<typeof invoiceSchema>", customMessage: profile);
            files["AGENTS.md"].ShouldNotContain("zod", Case.Insensitive, profile);
            files.Keys.ShouldContain(path => path.StartsWith("docs/adr/", StringComparison.Ordinal) && path.EndsWith("-frontend-validation-valibot.md", StringComparison.Ordinal), profile);
            files.Keys.ShouldNotContain(path => path.EndsWith("-frontend-validation-zod.md", StringComparison.Ordinal), profile);
        }
    }

    [Fact]
    public void With_react_hook_form_forms_resolve_with_valibotResolver()
    {
        var agents = Files(React, Valibot)["AGENTS.md"];

        agents.ShouldContain("`useForm({ resolver: valibotResolver(schema) })`");
        agents.ShouldNotContain("zodResolver");
    }

    [Fact]
    public void With_vee_validate_the_projects_own_toTypedSchema_adapts_valibot_and_lint_refuses_vee_validate_valibot()
    {
        var files = Files(Vue, Valibot);

        files[Adapter].ShouldContain("from 'valibot';");
        files[Adapter].ShouldContain("safeParseAsync(schema, values)");
        files[Adapter].ShouldNotContain("zod");
        files[Adapter].ShouldNotContain("throw new Error(");
        files[AdapterTest].ShouldContain("import * as v from 'valibot';");
        files[AdapterTest].ShouldNotContain("zod");
        files["web/app/eslint.config.js"].ShouldContain("'@vee-validate/valibot'");
        files["web/app/package.json"].ShouldNotContain("@vee-validate/");
        files["docs/adr/0033-frontend-forms-vee-validate.md"].ShouldContain("Valibot");
        files["docs/adr/0033-frontend-forms-vee-validate.md"].ShouldNotContain("zod", Case.Insensitive);
    }

    [Fact]
    public void With_valibot_the_react_agent_panel_and_dashboards_validate_with_valibot_and_name_no_zod()
    {
        var files = Files("saas-ai-react", Valibot);

        foreach (var path in ReactValidating)
        {
            files[path].ShouldContain("from 'valibot';", customMessage: path);
            files[path].ShouldNotContain("zod", Case.Insensitive, path);
            files[path].ShouldNotContain("throw new Error(", customMessage: path);
        }

        files["web/app/src/agents/AgentPanel.tsx"].ShouldContain("valibotResolver(messageSchema)");
        files["web/app/src/dashboards/DashboardCanvas.tsx"].ShouldContain("valibotResolver(nameSchema)");
        files["web/app/src/dashboards/DashboardAssistant.tsx"].ShouldContain("valibotResolver(questionSchema)");
        files["web/app/src/dashboards/proposal.ts"].ShouldContain("v.safeParse(proposalSchema, value)");
        WebSources(files).ShouldNotContain(source => source.Value.Contains("zod", StringComparison.Ordinal), "no web source names zod");
    }

    [Fact]
    public void With_valibot_the_vue_agent_panel_and_dashboards_validate_with_valibot_and_name_no_zod()
    {
        var files = Files(Vue, Valibot, OpenAI, AgentFramework, AgUi, Dashboards);

        foreach (var path in VueValidating)
        {
            files[path].ShouldContain("from 'valibot';", customMessage: path);
            files[path].ShouldNotContain("zod", Case.Insensitive, path);
            files[path].ShouldNotContain("throw new Error(", customMessage: path);
        }

        files["web/app/src/agents/AgentConversation.vue"].ShouldContain("validationSchema: toTypedSchema(messageSchema),");
        files["web/app/src/dashboards/DashboardCanvas.vue"].ShouldContain("toTypedSchema(nameSchema)");
        files["web/app/src/dashboards/DashboardAssistant.vue"].ShouldContain("toTypedSchema(questionSchema)");
        files["web/app/src/dashboards/proposal.ts"].ShouldContain("v.safeParse(proposalSchema, value)");
        WebSources(files).ShouldNotContain(source => source.Value.Contains("zod", StringComparison.Ordinal), "no web source names zod");
        files.Single(file => file.Key.EndsWith("-frontend-dashboards-user-editable.md", StringComparison.Ordinal)).Value.ShouldContain("vee-validate and Valibot");
    }

    [Fact]
    public void A_react_form_validated_by_valibot_builds_lints_shows_its_errors_submits_and_lint_refuses_zod()
    {
        using var project = new GeneratedProject(React, Valibot);
        project.Write("web/app/src/pages/SignUpForm.tsx", ReactSignUpForm);
        project.Write("web/app/src/pages/SignUpForm.test.tsx", ReactSignUpFormTest);

        var testOutput = InstallBuildLintAndTest(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/pages/SignUpForm.test.tsx > an empty valibot sign-up shows the schema messages next to its fields", customMessage: testOutput);
        plain.ShouldContain("✓ src/pages/SignUpForm.test.tsx > a valid valibot sign-up submits the values the schema puts out", customMessage: testOutput);
        var lintOutput = LintRefuses(project, OtherSchemaLibraries);
        lintOutput.ShouldContain("'zod' import is restricted from being used by a pattern. Use valibot (docs/adr)");
        lintOutput.ShouldContain("'yup' import is restricted from being used by a pattern. Use valibot (docs/adr)");
    }

    [Fact]
    public void A_vue_form_validated_by_valibot_builds_lints_shows_its_errors_submits_and_lint_refuses_zod_and_vee_validate_valibot()
    {
        using var project = new GeneratedProject(Vue, Valibot);
        project.Write("web/app/src/pages/SignUpForm.vue", VueSignUpForm);
        project.Write("web/app/src/pages/SignUpForm.test.ts", VueSignUpFormTest);

        var testOutput = InstallBuildLintAndTest(project);

        var plain = Escapes().Replace(testOutput, string.Empty);
        plain.ShouldContain("✓ src/forms/toTypedSchema.test.ts > a schema's messages reach the fields they belong to, nested and listed ones too", customMessage: testOutput);
        plain.ShouldContain("✓ src/forms/toTypedSchema.test.ts > valid values come back as the schema puts them out", customMessage: testOutput);
        plain.ShouldContain("✓ src/pages/SignUpForm.test.ts > an empty valibot sign-up shows the schema messages next to its fields", customMessage: testOutput);
        plain.ShouldContain("✓ src/pages/SignUpForm.test.ts > a valid valibot sign-up submits the values the schema puts out", customMessage: testOutput);
        var lintOutput = LintRefuses(project, OtherSchemaLibraries + "\nexport { toTypedSchema } from '@vee-validate/valibot';\n");
        lintOutput.ShouldContain("'zod' import is restricted from being used by a pattern. Use valibot (docs/adr)");
        lintOutput.ShouldContain("'@vee-validate/valibot' import is restricted from being used by a pattern. Use vee-validate with toTypedSchema from src/forms/toTypedSchema (docs/adr)");
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty($"{profile} {string.Join(' ', overrides)}");
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

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

    private static string LintRefuses(GeneratedProject project, string source)
    {
        project.Write("web/app/src/pages/OtherLibraries.ts", source);
        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web"), "pnpm", "lint");
        exitCode.ShouldNotBe(0, output);
        return output;
    }

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Escapes();
}
