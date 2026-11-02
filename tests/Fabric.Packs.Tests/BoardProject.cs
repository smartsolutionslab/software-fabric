namespace Fabric.Packs.Tests;

/// <summary>A generated project in a git repository, shared: each test points its origin at a fresh bare repository.</summary>
public sealed class BoardProject : IDisposable
{
    internal static readonly Dictionary<string, string> GitIdentity = new()
    {
        ["GIT_AUTHOR_NAME"] = "Agent",
        ["GIT_AUTHOR_EMAIL"] = "agent@example.com",
        ["GIT_COMMITTER_NAME"] = "Agent",
        ["GIT_COMMITTER_EMAIL"] = "agent@example.com",
    };

    public BoardProject()
    {
        Git(Project.ProjectDirectory, "init", "--quiet", "--initial-branch=main");
        Git(Project.ProjectDirectory, "add", "--all");
        Git(Project.ProjectDirectory, "commit", "--quiet", "--message", "generated");
    }

    internal GeneratedProject Project { get; } = new("saas-standard-react");

    internal static string Git(string directory, params string[] arguments) => Git(directory, GitIdentity, arguments);

    internal static string Git(string directory, IReadOnlyDictionary<string, string> environment, params string[] arguments)
    {
        var (exitCode, output) = Shell.Run(directory, environment, "git", arguments);
        exitCode.ShouldBe(0, $"git {string.Join(' ', arguments)}:\n{output}");
        return output.Trim();
    }

    public void Dispose() => Project.Dispose();
}
