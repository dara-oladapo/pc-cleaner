using System.Text;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Elevation;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Tests;

public class ElevatedOperationCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTripsHostileStrings()
    {
        var op = new ElevatedOperation(
            ElevatedOperationKind.SetMachineRunEntry,
            Enabled: true,
            ["a \"quoted\" name & | > %PATH% ; rm -rf / 'x' \\ ünïcødé"]);

        string token = ElevatedOperationCodec.Encode(op);

        Assert.True(ElevatedOperationCodec.TryDecode(token, out ElevatedOperation? decoded));
        Assert.NotNull(decoded);
        Assert.Equal(op.Kind, decoded.Kind);
        Assert.Equal(op.Enabled, decoded.Enabled);
        Assert.Equal(op.Targets, decoded.Targets);
    }

    [Fact]
    public void Encode_ProducesOnlyCommandLineSafeCharacters()
    {
        var op = new ElevatedOperation(ElevatedOperationKind.CleanJunkRoots, false, ["C:\\Windows\\Temp", "/Library/Caches", "\" --evil"]);

        string token = ElevatedOperationCodec.Encode(op);

        Assert.All(token, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_', $"unexpected '{c}'"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("a b")]
    [InlineData("AAAA")]
    public void TryDecode_RejectsMalformedTokens(string? token)
    {
        Assert.False(ElevatedOperationCodec.TryDecode(token, out ElevatedOperation? op));
        Assert.Null(op);
    }

    [Theory]
    [InlineData("{\"Kind\":\"CleanJunkRoots\",\"Enabled\":false,\"Targets\":[]}")]
    [InlineData("{\"Kind\":\"CleanJunkRoots\",\"Enabled\":false}")]
    [InlineData("{\"Kind\":\"CleanJunkRoots\",\"Enabled\":false,\"Targets\":[\"\"]}")]
    [InlineData("{\"Kind\":\"RunArbitraryCommand\",\"Enabled\":false,\"Targets\":[\"x\"]}")]
    [InlineData("{\"Kind\":99,\"Enabled\":false,\"Targets\":[\"x\"]}")]
    public void TryDecode_RejectsWellFormedButInvalidPayloads(string json)
    {
        string token = System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

        Assert.False(ElevatedOperationCodec.TryDecode(token, out _));
    }

    [Fact]
    public void TryDecode_RejectsOversizedToken()
    {
        Assert.False(ElevatedOperationCodec.TryDecode(new string('A', ElevatedOperationCodec.MaxEncodedLength + 4), out _));
    }

    [Fact]
    public void Encode_ThrowsWhenTooLargeForTheCommandLine()
    {
        var op = new ElevatedOperation(ElevatedOperationKind.CleanJunkRoots, false, [new string('x', ElevatedOperationCodec.MaxEncodedLength)]);

        Assert.Throws<ArgumentException>(() => ElevatedOperationCodec.Encode(op));
    }

    [Fact]
    public void TryGetHelperPayload_FindsTokenAfterVerb()
    {
        Assert.True(ElevatedOperationCodec.TryGetHelperPayload(["app.exe", ElevatedOperationCodec.HelperVerb, "abc"], out string? token));
        Assert.Equal("abc", token);
    }

    [Fact]
    public void TryGetHelperPayload_VerbWithoutTokenIsStillHelperMode()
    {
        Assert.True(ElevatedOperationCodec.TryGetHelperPayload(["app.exe", ElevatedOperationCodec.HelperVerb], out string? token));
        Assert.Null(token);
        Assert.False(ElevatedOperationCodec.TryDecode(token, out _));
    }

    [Fact]
    public void TryGetHelperPayload_NormalLaunchIsNotHelperMode()
    {
        Assert.False(ElevatedOperationCodec.TryGetHelperPayload(["app.exe"], out _));
    }

    [Theory]
    [InlineData(0, ElevationStatus.Succeeded)]
    [InlineData(1, ElevationStatus.Failed)]
    [InlineData(2, ElevationStatus.Rejected)]
    [InlineData(3, ElevationStatus.PartiallySucceeded)]
    [InlineData(-1073741510, ElevationStatus.Failed)]
    public void ExitCodes_MapToStatus(int exitCode, ElevationStatus expected)
    {
        Assert.Equal(expected, ElevationExitCodes.ToStatus(exitCode));
    }
}

public class ElevationPolicyTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "PcCleanerPolicy");
    private static readonly string RootA = Path.Combine(Base, "Temp");
    private static readonly string RootB = Path.Combine(Base, "SoftwareDistribution", "Download");

    [Fact]
    public void IsAllowedRoot_AcceptsExactRoot_AndTrailingSeparator()
    {
        Assert.True(ElevationPolicy.IsAllowedRoot(RootA, [RootA, RootB]));
        Assert.True(ElevationPolicy.IsAllowedRoot(RootA + Path.DirectorySeparatorChar, [RootA]));
    }

    [Fact]
    public void IsAllowedRoot_RejectsChildParentSiblingAndTraversal()
    {
        Assert.False(ElevationPolicy.IsAllowedRoot(Path.Combine(RootA, "child"), [RootA]));
        Assert.False(ElevationPolicy.IsAllowedRoot(Base, [RootA]));
        Assert.False(ElevationPolicy.IsAllowedRoot(RootA + "Evil", [RootA]));
        Assert.False(ElevationPolicy.IsAllowedRoot(Path.Combine(RootA, "..", "Temp"), [RootA]));
        Assert.False(ElevationPolicy.IsAllowedRoot(Path.Combine(RootA, ".", "x"), [RootA]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    [InlineData("relative\\path")]
    public void TryNormalizeAbsolutePath_RejectsNonAbsolute(string? path)
    {
        Assert.False(ElevationPolicy.TryNormalizeAbsolutePath(path, out _));
    }

    [Fact]
    public void TryNormalizeAbsolutePath_RejectsControlCharacters()
    {
        Assert.False(ElevationPolicy.TryNormalizeAbsolutePath(RootA + "\n", out _));
        Assert.False(ElevationPolicy.TryNormalizeAbsolutePath(RootA + "\0x", out _));
    }

    [Fact]
    public void FindContainingRoots_ReturnsOnlyRootsWithFailuresUnderThem()
    {
        string[] failed =
        [
            Path.Combine(RootB, "sub", "a.cab"),
            Path.Combine(RootB, "b.cab"),
            Path.Combine(Base, "Elsewhere", "c.tmp"),
            RootA + "Evil" + Path.DirectorySeparatorChar + "d.tmp",
            "not/absolute",
        ];

        IReadOnlyList<string> roots = ElevationPolicy.FindContainingRoots(failed, [RootA, RootB]);

        Assert.Equal([RootB], roots);
    }

    [Fact]
    public void FindContainingRoots_RootItselfIsNotUnderItself()
    {
        Assert.Empty(ElevationPolicy.FindContainingRoots([RootA], [RootA]));
    }

    [Theory]
    [InlineData("OneDrive", true)]
    [InlineData("Adobe Updater (x86)", true)]
    [InlineData("name\\with\\backslashes", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("bad\nname", false)]
    [InlineData("bad\0name", false)]
    public void IsValidRegistryValueName(string name, bool expected)
    {
        Assert.Equal(expected, ElevationPolicy.IsValidRegistryValueName(name));
    }

    [Fact]
    public void IsValidRegistryValueName_RejectsOverlongNames()
    {
        Assert.False(ElevationPolicy.IsValidRegistryValueName(new string('a', ElevationPolicy.MaxNameLength + 1)));
    }

    [Theory]
    [InlineData("Zoom.lnk", true)]
    [InlineData("com.example.agent.plist", true)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("a/b.lnk", false)]
    [InlineData("a\\b.lnk", false)]
    [InlineData("C:x.lnk", false)]
    [InlineData("file.lnk:stream", false)]
    [InlineData("wild*.lnk", false)]
    [InlineData("trailing.", false)]
    [InlineData("trailing ", false)]
    [InlineData("ctrl\u0001.lnk", false)]
    [InlineData("", false)]
    public void IsPlainFileName(string name, bool expected)
    {
        Assert.Equal(expected, ElevationPolicy.IsPlainFileName(name));
    }

    [Fact]
    public void IsFileDirectlyIn_AcceptsPlainFileInAllowedDirectory()
    {
        string dir = Path.Combine(Base, "LaunchDaemons");
        string file = Path.Combine(dir, "com.example.daemon.plist");

        Assert.True(ElevationPolicy.IsFileDirectlyIn(file, ".plist", [dir], out string matched));
        Assert.Equal(dir, matched);
    }

    [Fact]
    public void IsFileDirectlyIn_RejectsSubfolderWrongExtensionTraversalAndOtherDirectory()
    {
        string dir = Path.Combine(Base, "LaunchDaemons");

        Assert.False(ElevationPolicy.IsFileDirectlyIn(Path.Combine(dir, "sub", "x.plist"), ".plist", [dir], out _));
        Assert.False(ElevationPolicy.IsFileDirectlyIn(Path.Combine(dir, "x.sh"), ".plist", [dir], out _));
        Assert.False(ElevationPolicy.IsFileDirectlyIn(Path.Combine(dir, ".plist"), ".plist", [dir], out _));
        Assert.False(ElevationPolicy.IsFileDirectlyIn(Path.Combine(dir, "..", "LaunchDaemons", "x.plist"), ".plist", [dir], out _));
        Assert.False(ElevationPolicy.IsFileDirectlyIn(Path.Combine(Base, "Other", "x.plist"), ".plist", [dir], out _));
    }
}

public class ShellQuotingTests
{
    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("with space", "'with space'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("$(rm -rf /); `x` \"q\" \\", "'$(rm -rf /); `x` \"q\" \\'")]
    [InlineData("", "''")]
    public void QuotePosix(string input, string expected)
    {
        Assert.Equal(expected, ShellQuoting.QuotePosix(input));
    }

    [Fact]
    public void QuotePosix_RejectsNul()
    {
        Assert.Throws<ArgumentException>(() => ShellQuoting.QuotePosix("a\0b"));
    }

    [Fact]
    public void JoinPosix_QuotesEachArgument()
    {
        Assert.Equal("'/bin/mv' '/a b' '/c'\\''d'", ShellQuoting.JoinPosix("/bin/mv", "/a b", "/c'd"));
    }

    [Theory]
    [InlineData("abc", "\"abc\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("back\\slash", "\"back\\\\slash\"")]
    public void QuoteAppleScriptString(string input, string expected)
    {
        Assert.Equal(expected, ShellQuoting.QuoteAppleScriptString(input));
    }

    [Fact]
    public void BuildAdminAppleScript_EscapesShellCommandForAppleScript()
    {
        string shell = ShellQuoting.JoinPosix("/bin/mv", "/Library/LaunchDaemons/a\"b\\c'd.plist", "/x");

        string script = ShellQuoting.BuildAdminAppleScript(shell);

        Assert.Equal(
            "do shell script \"'/bin/mv' '/Library/LaunchDaemons/a\\\"b\\\\c'\\\\''d.plist' '/x'\" with administrator privileges",
            script);
    }
}

public class ElevatedJunkRetryTests
{
    [Fact]
    public async Task RunAsync_CountsOnlyFilesActuallyRemoved()
    {
        using var dir = new TestDirectory();
        string removed = dir.WriteFileOfSize("root/a.bin", 100);
        string kept = dir.WriteFileOfSize("root/b.bin", 40);
        var fake = new FakeElevation(ElevationStatus.PartiallySucceeded, onClean: () => File.Delete(removed));

        ElevatedJunkOutcome outcome = await ElevatedJunkRetry.RunAsync(fake, [Path.Combine(dir.Path, "root")], [removed, kept]);

        Assert.Equal(ElevationStatus.PartiallySucceeded, outcome.Status);
        Assert.Equal(100, outcome.BytesFreed);
        Assert.Equal([removed], outcome.RemovedPaths);
        Assert.Equal([kept], outcome.StillFailedPaths);
    }

    [Fact]
    public async Task RunAsync_Cancelled_ReportsNothingFreed()
    {
        using var dir = new TestDirectory();
        string file = dir.WriteFileOfSize("root/a.bin", 100);
        var fake = new FakeElevation(ElevationStatus.Cancelled, onClean: () => { });

        ElevatedJunkOutcome outcome = await ElevatedJunkRetry.RunAsync(fake, [Path.Combine(dir.Path, "root")], [file]);

        Assert.Equal(ElevationStatus.Cancelled, outcome.Status);
        Assert.Equal(0, outcome.BytesFreed);
        Assert.Empty(outcome.RemovedPaths);
        Assert.Equal([file], outcome.StillFailedPaths);
    }

    private sealed class FakeElevation(ElevationStatus status, Action onClean) : IElevationService
    {
        public bool CanElevate(StartupItem item) => false;

        public Task<ElevationResult> SetStartupItemEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken = default) =>
            Task.FromResult(ElevationResult.Rejected);

        public IReadOnlyList<string> GetElevatableJunkRoots(IEnumerable<string> failedPaths) => [];

        public Task<ElevationResult> CleanJunkRootsAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken = default)
        {
            onClean();
            return Task.FromResult(new ElevationResult(status));
        }
    }
}
