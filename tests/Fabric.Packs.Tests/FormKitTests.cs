using System.Text.RegularExpressions;
using Fabric.Core.Choices;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// frontend.forms.formkit (#269), by the owner's decisions there: unstyled FormKit 2.5.0 inputs whose sections take
/// their classes from the project's own <c>src/forms/formkit.config.ts</c>, a project-owned zod and Valibot adapter
/// (<c>schemaSubmit</c>) that puts the schema's messages on the fields with <c>node.setErrors</c>, and lint refusing
/// the other form libraries and <c>@formkit/zod</c>; and the FormKit branches of the Vue agent panel and dashboards.
/// </summary>
public sealed partial class FormKitTests
{
    private const string Vue = "saas-standard-vue";
    private const string FormKit = "frontend.forms=formkit";
    private const string Valibot = "frontend.validation=valibot";

    private static readonly string[] VueForms =
    [
        "web/app/src/agents/AgentConversation.vue",
        "web/app/src/dashboards/DashboardCanvas.vue",
        "web/app/src/dashboards/DashboardAssistant.vue",
    ];

    private static readonly string[] VueAgentAndDashboards =
    [
        "backend.llm=openai",
        "backend.llm.agents=agent-framework",
        "frontend.agent-ui=ag-ui",
        "frontend.dashboards=user-editable,generative",
    ];

    /// <summary>A form a developer would write with FormKit, the project's zod schema and its adapter.</summary>
    private const string SignUpForm = """
        <script setup lang="ts">
        import { FormKit } from '@formkit/vue';
        import { z } from 'zod';
        import { Button } from '@/components/ui/button';
        import { schemaSubmit } from '@/forms/schemaSubmit';

        const signUpSchema = z.object({
          name: z.string().trim().min(1, 'Enter a name'),
          email: z.email('Enter an email address'),
        });

        const emit = defineEmits<{ submitted: [values: z.infer<typeof signUpSchema>] }>();

        const submit = schemaSubmit(signUpSchema, (values) => emit('submitted', values));
        </script>

        <template>
          <FormKit type="form" :actions="false" :value="{ name: '', email: '' }" @submit="submit">
            <FormKit type="text" name="name" label="Name" />
            <FormKit type="email" name="email" label="Email" />
            <Button type="submit">Sign up</Button>
          </FormKit>
        </template>
        """;

    private const string SignUpFormTest = """
        import { render, screen, waitFor } from '@testing-library/vue';
        import userEvent from '@testing-library/user-event';
        import { expect, test } from 'vitest';
        import SignUpForm from './SignUpForm.vue';

        test('an empty FormKit sign-up shows the schema messages next to its fields', async () => {
          render(SignUpForm);

          await userEvent.click(screen.getByRole('button', { name: 'Sign up' }));

          expect(await screen.findByText('Enter a name')).toHaveAttribute('role', 'alert');
          expect(await screen.findByText('Enter an email address')).toHaveAttribute('role', 'alert');
          expect(screen.getByLabelText('Name')).toHaveAccessibleDescription('Enter a name');
        });

        test('a valid FormKit sign-up submits the values the schema puts out', async () => {
          const { emitted } = render(SignUpForm);

          await userEvent.type(screen.getByLabelText('Name'), '  Ada  ');
          await userEvent.type(screen.getByLabelText('Email'), 'ada@example.com');
          await userEvent.click(screen.getByRole('button', { name: 'Sign up' }));

          await waitFor(() => expect(emitted('submitted')).toEqual([[{ name: 'Ada', email: 'ada@example.com' }]]));
          expect(screen.queryByRole('alert')).not.toBeInTheDocument();
        });
        """;

    private const string OtherVueFormLibraries = """
        import { useForm as useTanStackForm } from '@tanstack/vue-form';
        import { createZodPlugin } from '@formkit/zod';
        import { generateClasses } from '@formkit/themes';
        import '@formkit/themes/genesis';
        import { toTypedSchema } from '@vee-validate/zod';
        import { useForm } from 'vee-validate';

        export const others = [useTanStackForm, createZodPlugin, generateClasses, toTypedSchema, useForm];
        """;

    [Fact]
    public void With_react_formkit_is_refused_in_one_sentence()
    {
        var resolution = ChoiceResolver.Resolve(Repository.Catalog, "saas-standard-react", [FormKit]);

        resolution.Choices.ShouldNotBeNull();
        RuleEvaluator.Check(Repository.Catalog, resolution.Choices)
            .ShouldBe(["frontend.forms=formkit requires frontend.framework == vue, but frontend.framework is react."]);
    }

    [Fact]
    public void FormKit_replaces_vee_validate_and_its_adapter_in_the_dependencies_the_rules_the_adr_and_the_lint_rule()
    {
        var files = Files(Vue, FormKit);

        files["web/app/package.json"].ShouldContain("\"@formkit/vue\": \"2.5.0\"");
        files["web/app/package.json"].ShouldContain("\"@formkit/core\": \"2.5.0\"");
        files["web/app/package.json"].ShouldContain("\"@vue/test-utils\": \"2.5.1\"");
        files["web/app/package.json"].ShouldNotContain("vee-validate");
        files["web/app/package.json"].ShouldNotContain("@formkit/themes");
        files["web/app/package.json"].ShouldNotContain("@formkit/zod");
        files.Keys.ShouldNotContain(path => path.Contains("toTypedSchema", StringComparison.Ordinal));
        files.Keys.ShouldContain("web/app/src/forms/formkit.config.ts");
        files.Keys.ShouldContain("web/app/src/forms/schemaSubmit.ts");
        files.Keys.ShouldContain("web/app/src/forms/schemaSubmit.test.ts");
        files["web/app/eslint.config.js"].ShouldContain("group: ['vee-validate', '@vee-validate/*', '@tanstack/vue-form', '@formkit/zod'],");
        files["web/app/eslint.config.js"].ShouldContain("message: 'Use FormKit with schemaSubmit from src/forms/schemaSubmit (docs/adr).'");
        files["web/app/eslint.config.js"].ShouldContain("group: ['@formkit/themes', '@formkit/themes/*'],");
        files["web/app/eslint.config.js"].ShouldContain("message: 'Style FormKit in src/forms/formkit.config.ts (docs/adr).'");
        files["web/app/eslint.config.js"].ShouldNotContain("Use vee-validate");
        files["web/app/src/main.ts"].ShouldContain("import { withFormKit } from './providers/formkit';");
        files["web/app/src/main.ts"].ShouldContain("withFormKit");
        files["web/app/src/providers/formkit.ts"].ShouldContain("app.use(plugin, config);");
        files["web/app/src/test-setup.ts"].ShouldContain("config.global.plugins.push([formkit, formkitConfig]);");
        files["AGENTS.md"].ShouldContain("## Forms: FormKit");
        files["AGENTS.md"].ShouldContain("`schemaSubmit(schema, handler)`");
        files["AGENTS.md"].ShouldNotContain("## Forms: vee-validate");
        files["AGENTS.md"].ShouldNotContain("toTypedSchema");
        Adr(files).ShouldContain("FormKit 2.5.0");
        Adr(files).ShouldContain("src/forms/formkit.config.ts");
    }

    [Fact]
    public void The_formkit_config_gives_the_inputs_shadcn_vues_classes_and_the_messages_the_alert_role_without_a_formkit_theme()
    {
        var config = Files(Vue, FormKit)["web/app/src/forms/formkit.config.ts"];

        config.ShouldContain("import { defaultConfig } from '@formkit/vue';");
        config.ShouldContain("border-input");
        config.ShouldContain("text-destructive");
        config.ShouldContain("rootClasses");
        config.ShouldContain("message: { $el: 'p', attrs: { role: 'alert' } }");
        config.ShouldNotContain("@formkit/themes");
        config.ShouldNotContain("genesis");
        config.ShouldNotContain("throw new Error(");
    }

    [Fact]
    public void With_reka_ui_the_formkit_config_names_tailwinds_palette_as_base_button_does_not_shadcn_vues_theme()
    {
        var config = Files(Vue, FormKit, "frontend.ui-library=reka-ui")["web/app/src/forms/formkit.config.ts"];

        config.ShouldContain("border-slate-300");
        config.ShouldContain("text-red-600");
        config.ShouldNotContain("border-input");
        config.ShouldNotContain("text-destructive");
        config.ShouldNotContain("throw new Error(");
    }

    [Fact]
    public void With_valibot_the_adapter_parses_with_valibot_and_nothing_names_zod()
    {
        var files = Files(Vue, FormKit, Valibot);

        files["web/app/src/forms/schemaSubmit.ts"].ShouldContain("v.safeParseAsync(schema, values)");
        files["web/app/src/forms/schemaSubmit.test.ts"].ShouldContain("v.object(");
        files["web/app/src/forms/schemaSubmit.ts"].ShouldNotContain("zod", Case.Insensitive);
        files["AGENTS.md"].ShouldContain("## Forms: FormKit");
        Adr(files).ShouldNotContain("zod", Case.Insensitive);
        files["web/app/eslint.config.js"].ShouldContain("group: ['vee-validate', '@vee-validate/*', '@tanstack/vue-form', '@formkit/zod'],");
    }

    [Theory]
    [InlineData("zod", "z.object(")]
    [InlineData("valibot", "v.object(")]
    public void The_vue_agent_panel_and_dashboards_hold_formkit_forms_and_name_no_other_form_library(string validation, string schema)
    {
        var files = Files(Vue, [FormKit, $"frontend.validation={validation}", .. VueAgentAndDashboards]);

        foreach (var path in VueForms)
        {
            files[path].ShouldContain("import { FormKit } from '@formkit/vue';", customMessage: path);
            files[path].ShouldContain("import { schemaSubmit } from '@/forms/schemaSubmit';", customMessage: path);
            files[path].ShouldContain("<FormKit type=\"form\" :actions=\"false\"", customMessage: path);
            files[path].ShouldContain(schema, customMessage: path);
            files[path].ShouldNotContain("throw new Error(", customMessage: path);
        }

        files["web/app/src/agents/AgentConversation.vue"].ShouldContain("schemaSubmit(messageSchema, ");
        files["web/app/src/agents/AgentConversation.vue"].ShouldContain("<FormKit type=\"textarea\" name=\"message\" label=\"Message\"");
        files["web/app/src/dashboards/DashboardCanvas.vue"].ShouldContain("schemaSubmit(nameSchema, ");
        files["web/app/src/dashboards/DashboardCanvas.vue"].ShouldContain("schemaSubmit(widgetSchema, ");
        files["web/app/src/dashboards/DashboardCanvas.vue"].ShouldContain("<FormKit type=\"select\" name=\"source\" label=\"Widget\"");
        files["web/app/src/dashboards/DashboardAssistant.vue"].ShouldContain("schemaSubmit(questionSchema, ");
        WebSources(files).ShouldNotContain(source => Regex.IsMatch(source.Value, "vee-validate|toTypedSchema|@tanstack/vue-form"), "no web source names vee-validate, its adapter or TanStack Form");
        Adr(files, "agent-ui-ag-ui").ShouldContain("shadcn-vue components and FormKit");
        Adr(files, "dashboards-user-editable").ShouldContain("with FormKit and");
        files["AGENTS.md"].ShouldContain("and the FormKit forms)");
    }

    [Fact]
    public void With_another_vue_ui_library_the_formkit_screens_fail_saying_what_they_are_built_from()
    {
        var files = Files(Vue, [FormKit, "frontend.ui-library=reka-ui", .. VueAgentAndDashboards]);

        files["web/app/src/agents/AgentConversation.vue"].ShouldStartWith("<script lang=\"ts\">\nthrow new Error('The ag-ui pack builds the Vue agent panel from shadcn-vue with vee-validate, TanStack Form or FormKit and zod or valibot only so far; frontend.ui-library is reka-ui, frontend.forms is formkit");
        files["web/app/src/dashboards/DashboardCanvas.vue"].ShouldStartWith("<script lang=\"ts\">\nthrow new Error('The dashboards pack builds the Vue dashboard page from shadcn-vue with vee-validate, TanStack Form or FormKit and zod or valibot only so far; frontend.ui-library is reka-ui, frontend.forms is formkit");
        files["web/app/src/dashboards/DashboardAssistant.vue"].ShouldStartWith("<script lang=\"ts\">\nthrow new Error('The generative dashboards pack builds the Vue assistant from shadcn-vue with vee-validate, TanStack Form or FormKit and zod or valibot only so far; frontend.ui-library is reka-ui, frontend.forms is formkit");
    }

    [Theory]
    [InlineData("zod")]
    [InlineData("valibot")]
    public void A_formkit_form_builds_lints_shows_its_errors_submits_its_adapter_passes_and_lint_refuses_the_other_form_libraries(string validation)
    {
        using var project = new GeneratedProject(Vue, FormKit, $"frontend.validation={validation}");
        project.Write("web/app/src/pages/SignUpForm.vue", validation == "zod" ? SignUpForm : ToValibot(SignUpForm));
        project.Write("web/app/src/pages/SignUpForm.test.ts", SignUpFormTest);

        var testOutput = Plain(InstallBuildLintAndTest(project));

        testOutput.ShouldContain("✓ src/pages/SignUpForm.test.ts > an empty FormKit sign-up shows the schema messages next to its fields");
        testOutput.ShouldContain("✓ src/pages/SignUpForm.test.ts > a valid FormKit sign-up submits the values the schema puts out");
        testOutput.ShouldContain("✓ src/forms/schemaSubmit.test.ts > a schema's messages land on the fields they belong to, nested and listed ones too");
        testOutput.ShouldContain("✓ src/forms/schemaSubmit.test.ts > a valid form hands the handler the schema's output");
        testOutput.ShouldContain("✓ src/forms/schemaSubmit.test.ts > fields named like an object's own properties get their messages too");
        testOutput.ShouldContain("✓ src/forms/schemaSubmit.test.ts > a second submit takes away the messages the schema no longer gives");
        FormatCheck(project, "src/forms");
        var lintOutput = LintRefuses(project, OtherVueFormLibraries);
        const string Message = "import is restricted from being used by a pattern. Use FormKit with schemaSubmit from src/forms/schemaSubmit (docs/adr)";
        lintOutput.ShouldContain($"'@tanstack/vue-form' {Message}");
        lintOutput.ShouldContain($"'@formkit/zod' {Message}");
        lintOutput.ShouldContain("'@formkit/themes' import is restricted from being used by a pattern. Style FormKit in src/forms/formkit.config.ts (docs/adr)");
        lintOutput.ShouldContain("'@formkit/themes/genesis' import is restricted from being used by a pattern. Style FormKit in src/forms/formkit.config.ts (docs/adr)");
        lintOutput.ShouldContain($"'@vee-validate/zod' {Message}");
        lintOutput.ShouldContain($"'vee-validate' {Message}");
    }

    [Fact]
    public void The_vue_agent_panel_and_dashboards_on_formkit_build_lint_pass_their_own_tests_and_stay_formatted()
    {
        using var project = new GeneratedProject(Vue, [FormKit, .. VueAgentAndDashboards]);

        var testOutput = Plain(InstallBuildLintAndTest(project));

        testOutput.ShouldContain("✓ src/agents/AgentPanel.test.ts > ");
        testOutput.ShouldContain("✓ src/dashboards/DashboardEditor.test.ts > a new dashboard gets a fresh time-ordered id and needs a name before it is saved");
        testOutput.ShouldContain("✓ src/dashboards/DashboardEditor.test.ts > paging the widget picker clears the choice, so a widget of one page is never added as a visual of another");
        testOutput.ShouldContain("✓ src/dashboards/DashboardAssistant.test.ts > ");
        testOutput.ShouldNotContain(" × ");
        FormatCheck(project);
    }

    /// <summary>The sign-up form with the same rules written in Valibot.</summary>
    private static string ToValibot(string form) => form
        .Replace("import { z } from 'zod';", "import * as v from 'valibot';", StringComparison.Ordinal)
        .Replace(
            """
            const signUpSchema = z.object({
              name: z.string().trim().min(1, 'Enter a name'),
              email: z.email('Enter an email address'),
            });
            """,
            """
            const signUpSchema = v.object({
              name: v.pipe(v.string(), v.trim(), v.minLength(1, 'Enter a name')),
              email: v.pipe(v.string(), v.email('Enter an email address')),
            });
            """,
            StringComparison.Ordinal)
        .Replace("z.infer<typeof signUpSchema>", "v.InferOutput<typeof signUpSchema>", StringComparison.Ordinal);

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty($"{profile} {string.Join(' ', overrides)}");
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>The generated ADR of an option, by the end of its file name; the forms option's by default.</summary>
    private static string Adr(Dictionary<string, string> files, string option = "forms-formkit") =>
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

    /// <summary>Checks the format of the generated files under one path only, leaving out a test's own fixtures.</summary>
    private static void FormatCheck(GeneratedProject project, string path)
    {
        var (exitCode, output) = Shell.Run(Path.Combine(project.ProjectDirectory, "web", "app"), "pnpm", "exec", "prettier", "--check", path);
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
