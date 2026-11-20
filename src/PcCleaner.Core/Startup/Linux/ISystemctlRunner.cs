using System.ComponentModel;
using System.Diagnostics;

namespace PcCleaner.Core.Startup.Linux;

/// <summary>The result of one <c>systemctl</c> invocation. <see cref="Started"/> is false if it could not be run at all.</summary>
public sealed record SystemctlResult(bool Started, int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => Started && ExitCode == 0;

    public static SystemctlResult NotStarted(string reason) => new(false, -1, string.Empty, reason);
}

/// <summary>Runs <c>systemctl</c> with an argument list. Abstracted so the logic around it is testable without systemd.</summary>
public interface ISystemctlRunner
{
    Task<SystemctlResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

/// <summary>
/// Starts the real <c>systemctl</c>. Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, one element per
/// argument, never through a shell or a single command string, so a unit name can't be interpreted as anything else.
/// </summary>
public sealed class ProcessSystemctlRunner : ISystemctlRunner
{
    private readonly string _fileName;
    private readonly TimeSpan _timeout;

    public ProcessSystemctlRunner(string fileName = "systemctl", TimeSpan? timeout = null)
    {
        _fileName = fileName;
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
    }

    public async Task<SystemctlResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(_fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Stable, parseable output: no pager, no colour, no localisation of messages we might show.
        startInfo.Environment["SYSTEMD_PAGER"] = string.Empty;
        startInfo.Environment["SYSTEMD_COLORS"] = "0";
        startInfo.Environment["LC_ALL"] = "C.UTF-8";

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("systemctl did not start.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return SystemctlResult.NotStarted(ex.Message);
        }

        using (process)
        {
            process.StandardInput.Close();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                cancellationToken.ThrowIfCancellationRequested();
                return new SystemctlResult(true, -1, string.Empty, $"systemctl did not finish within {_timeout.TotalSeconds:0} seconds.");
            }

            return new SystemctlResult(true, process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
    }
}
