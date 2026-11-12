namespace PcCleaner.Core.Elevation;

/// <summary>
/// Builds the macOS <c>osascript</c> program that runs a fixed shell command as root. Pure string work, so the
/// quoting — the part that keeps a hostile file name from becoming a command — is unit-tested on every OS.
/// </summary>
/// <remarks>
/// Two layers of quoting apply. Each argument is POSIX single-quoted (the only shell quoting with no special
/// characters inside, bar the quote itself), then the whole command is embedded as an AppleScript string literal,
/// which escapes backslash and double quote. The script reaches osascript as one argv entry — never via a shell.
/// </remarks>
public static class ShellQuoting
{
    /// <summary>Single-quotes <paramref name="value"/> for <c>/bin/sh</c>. An embedded <c>'</c> becomes <c>'\''</c>.</summary>
    public static string QuotePosix(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        RejectNul(value);
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    /// <summary>Quotes every element and joins them into one shell command line.</summary>
    public static string JoinPosix(params IEnumerable<string> argv) => string.Join(' ', argv.Select(QuotePosix));

    /// <summary>Wraps <paramref name="value"/> as an AppleScript string literal.</summary>
    public static string QuoteAppleScriptString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        RejectNul(value);
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>The AppleScript that asks for an admin password and then runs <paramref name="shellCommand"/> as root.</summary>
    public static string BuildAdminAppleScript(string shellCommand) =>
        $"do shell script {QuoteAppleScriptString(shellCommand)} with administrator privileges";

    private static void RejectNul(string value)
    {
        if (value.Contains('\0'))
        {
            throw new ArgumentException("Value contains a NUL character.", nameof(value));
        }
    }
}
