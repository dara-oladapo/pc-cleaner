using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCleaner.Core.Elevation;

/// <summary>The fixed set of operations the elevated helper will perform. Anything else is rejected.</summary>
public enum ElevatedOperationKind
{
    /// <summary>Delete the contents of system-owned junk roots. Targets: the root directories.</summary>
    CleanJunkRoots,

    /// <summary>Move a value between the all-users Run key and PC Cleaner's all-users quarantine key. Target: the value name.</summary>
    SetMachineRunEntry,

    /// <summary>Rename a shortcut in the all-users Startup folder to/from its ".disabled" name. Target: the active file name.</summary>
    SetCommonStartupShortcut,
}

/// <summary>One request to the elevated helper. Carries names and paths only, never a command to run.</summary>
public sealed record ElevatedOperation(ElevatedOperationKind Kind, bool Enabled, IReadOnlyList<string> Targets);

/// <summary>
/// Encodes an <see cref="ElevatedOperation"/> into a single command-line token and back.
/// </summary>
/// <remarks>
/// The payload is JSON, then base64url, so the token only ever contains <c>[A-Za-z0-9_-]</c>. That sidesteps
/// command-line quoting entirely: there is no character a path or registry value name could contain that would
/// split the argument, inject another one, or be interpreted by a shell. Decoding is strict and size-capped, and
/// a decoded operation is still only a request — the elevated side validates every target before acting.
/// </remarks>
public static class ElevatedOperationCodec
{
    /// <summary>The command-line verb that switches the app into elevated-helper mode.</summary>
    public const string HelperVerb = "--pccleaner-elevated";

    /// <summary>Keeps the token well inside the Windows command-line limit.</summary>
    public const int MaxEncodedLength = 8192;

    private const int MaxTargets = 64;

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Encode(ElevatedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var dto = new Payload(operation.Kind, operation.Enabled, [.. operation.Targets]);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(dto, Options);
        string token = Base64Url.EncodeToString(json);

        if (token.Length > MaxEncodedLength)
        {
            throw new ArgumentException("The operation is too large to pass to the elevated helper.", nameof(operation));
        }

        return token;
    }

    public static bool TryDecode(string? token, out ElevatedOperation? operation)
    {
        operation = null;

        if (string.IsNullOrEmpty(token) || token.Length > MaxEncodedLength || !IsBase64UrlAlphabet(token))
        {
            return false;
        }

        try
        {
            byte[] json = Base64Url.DecodeFromChars(token);
            Payload? dto = JsonSerializer.Deserialize<Payload>(json, Options);
            if (dto?.Targets is null
                || !Enum.IsDefined(dto.Kind)
                || dto.Targets.Length == 0
                || dto.Targets.Length > MaxTargets
                || dto.Targets.Any(string.IsNullOrEmpty))
            {
                return false;
            }

            operation = new ElevatedOperation(dto.Kind, dto.Enabled, dto.Targets);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when <see cref="HelperVerb"/> appears in a process's arguments, i.e. the process was started as the
    /// elevated helper. <paramref name="token"/> is the argument after it, or null when the verb is last — the caller
    /// must still treat that as helper mode (and reject it) rather than fall through to launching the UI elevated.
    /// </summary>
    public static bool TryGetHelperPayload(IReadOnlyList<string> args, out string? token)
    {
        token = null;
        for (int i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], HelperVerb, StringComparison.Ordinal))
            {
                token = i + 1 < args.Count ? args[i + 1] : null;
                return true;
            }
        }

        return false;
    }

    private static bool IsBase64UrlAlphabet(string token)
    {
        foreach (char c in token)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record Payload(ElevatedOperationKind Kind, bool Enabled, string[] Targets);
}
