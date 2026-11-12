namespace Fabric.Cli.Tests;

/// <summary>Runs git in a test's project, without the machine's signing or identity settings.</summary>
internal static class Git
{
    public static string Run(string directory, params string[] arguments)
    {
        var (exitCode, output, error) = TryRun(directory, arguments);
        exitCode.ShouldBe(0, $"git {string.Join(' ', arguments)}: {error}");
        return output;
    }

    public static (int ExitCode, string Output, string Error) TryRun(string directory, params string[] arguments) =>
        Programs.Run("git", directory, ["-c", "user.name=test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", .. arguments]);

    /// <summary>Commits everything in <paramref name="directory"/>.</summary>
    public static void Commit(string directory, string message)
    {
        Run(directory, "add", "-A");
        Run(directory, "commit", "-q", "-m", message);
    }
}
