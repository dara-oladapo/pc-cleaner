using System.Diagnostics;
using PcCleaner.Core.DevCleanup;

namespace PcCleaner.Core.Tests.DevCleanup;

/// <summary>A process runner that answers from a script and records every command it was asked to run.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<string, ProcessResult?> _respond;

    public FakeProcessRunner(Func<string, ProcessResult?> respond)
    {
        _respond = respond;
    }

    public List<string> Calls { get; } = [];

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string commandLine = string.Join(' ', [fileName, .. arguments]);
        Calls.Add(commandLine);
        return Task.FromResult(_respond(commandLine) ?? ProcessResult.NotStarted($"not scripted: {commandLine}"));
    }

    public static ProcessResult Ok(string stdout = "") => new(0, stdout, string.Empty);

    public static ProcessResult Fail(string stderr, int exitCode = 1) => new(exitCode, string.Empty, stderr);
}

internal sealed class FixedTime(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}

/// <summary>Runs the real git CLI inside a test's own temp directory, with a fixed identity and no signing or hooks.</summary>
internal static class TestGit
{
    public static string Run(string directory, params string[] arguments)
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in (string[])["-c", "commit.gpgsign=false", "-c", "core.hooksPath=", "-c", "core.autocrlf=false", "-C", directory, .. arguments])
        {
            psi.ArgumentList.Add(a);
        }

        psi.Environment["GIT_AUTHOR_NAME"] = "Test";
        psi.Environment["GIT_AUTHOR_EMAIL"] = "test@example.invalid";
        psi.Environment["GIT_COMMITTER_NAME"] = "Test";
        psi.Environment["GIT_COMMITTER_EMAIL"] = "test@example.invalid";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using Process process = Process.Start(psi)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {stderr}");
        }

        return stdout.Result;
    }

    /// <summary>
    /// A bare "origin", a main repo with one pushed commit on main, inside <paramref name="dir"/>.
    /// Returns the main repo's path.
    /// </summary>
    public static string CreatePushedRepo(TestDirectory dir, string gitignore = "")
    {
        string origin = Path.Combine(dir.Path, "origin.git");
        string repo = Path.Combine(dir.Path, "repo");
        Directory.CreateDirectory(origin);
        Directory.CreateDirectory(repo);
        Run(origin, "init", "--bare", "-b", "main");
        Run(repo, "init", "-b", "main");
        File.WriteAllText(Path.Combine(repo, "README.md"), "hello");
        if (gitignore.Length > 0)
        {
            File.WriteAllText(Path.Combine(repo, ".gitignore"), gitignore);
        }

        Run(repo, "add", "-A");
        Run(repo, "commit", "-m", "initial");
        Run(repo, "remote", "add", "origin", origin);
        Run(repo, "push", "-u", "origin", "main");
        return repo;
    }
}
