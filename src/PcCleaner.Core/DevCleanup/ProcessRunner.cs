using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace PcCleaner.Core.DevCleanup;

/// <summary>What a finished external command produced. Output is captured, never parsed for meaning here.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Exit code used when the program could not be started at all (not installed, not on PATH).</summary>
    public const int NotStartedExitCode = -1;

    public bool Succeeded => ExitCode == 0;

    public bool Started => ExitCode != NotStartedExitCode;

    public static ProcessResult NotStarted(string reason) => new(NotStartedExitCode, string.Empty, reason);

    /// <summary>Stderr if there is any, else stdout: whatever the tool said about why it failed, trimmed.</summary>
    public string ErrorText =>
        (string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError).Trim();
}

/// <summary>
/// Runs an external command. Every Docker, WSL and git call in developer cleanup goes through this, so tests
/// substitute a fake and nothing in the test suite ever touches a real engine or repository it did not create.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/> passed as discrete arguments (never
    /// through a shell). A program that cannot be started yields <see cref="ProcessResult.NotStarted"/> rather
    /// than throwing. Cancellation kills the process tree and throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);
}

/// <summary><see cref="IProcessRunner"/> over <see cref="Process"/>.</summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A bare name is resolved here so a Finder-launched Mac app (minimal PATH) still finds Homebrew/Docker tools.
        string resolved = Path.IsPathRooted(fileName) ? fileName : ToolLocator.Find(fileName) ?? fileName;

        var psi = new ProcessStartInfo(resolved)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        // wsl.exe writes UTF-16 unless told otherwise; this makes its output readable as UTF-8.
        psi.Environment["WSL_UTF8"] = "1";

        // Read-only git commands (status) may otherwise refresh and lock the index of a repo in use.
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        // Never block waiting for credentials on a hidden console.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return ProcessResult.NotStarted($"Could not start '{fileName}': {ex.Message}");
        }

        using (process)
        {
            process.StandardInput.Close();

            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // Already exited.
                }
            });

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            string output = await stdout.ConfigureAwait(false);
            string error = await stderr.ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            return new ProcessResult(process.ExitCode, Clean(output), Clean(error));
        }
    }

    // Older wsl.exe builds ignore WSL_UTF8 and still emit UTF-16; read as UTF-8 that shows up as NULs.
    private static string Clean(string text) => text.Replace("\0", string.Empty, StringComparison.Ordinal).TrimStart('﻿');
}
