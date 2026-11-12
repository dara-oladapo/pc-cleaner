#if DEBUG
using System.Runtime.CompilerServices;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Desktop.Diagnostics;

/// <summary>
/// Debug-only stand-in for the Linux junk scanner (#10), selected by <c>PCCLEANER_FAKE_SCAN=1</c>.
/// </summary>
/// <remarks>
/// Yields a fixed set of invented results, one at a time with a short delay, so the junk screen's streaming
/// <c>ObservableCollection</c>, grouping, meters, and selection totals can be seen working under Avalonia.
/// It never reads or writes the file system: every path is under a directory that does not exist and is
/// never created, and the scaffold's cleaner is inert anyway.
/// </remarks>
internal sealed class FakeJunkScanner : IJunkScanner
{
    private const string Root = "/nonexistent/pc-cleaner-fake-scan";

    private static readonly JunkItem[] Items =
    [
        new($"{Root}/tmp", 412_000_000, JunkCategory.TempFiles, "Temporary files (synthetic)", true),
        new($"{Root}/.cache/mozilla/firefox", 982_000_000, JunkCategory.BrowserCache, "Firefox cache (synthetic)", true),
        new($"{Root}/.cache/google-chrome", 655_000_000, JunkCategory.BrowserCache, "Chrome cache (synthetic)", true),
        new($"{Root}/.cache/thumbnails", 88_000_000, JunkCategory.SystemCache, "Thumbnail cache (synthetic)", true),
        new($"{Root}/.cache/pip", 240_000_000, JunkCategory.PackageManagerCache, "pip cache (synthetic)", true),
        new($"{Root}/.npm/_cacache", 1_310_000_000, JunkCategory.PackageManagerCache, "npm cache (synthetic)", true),
        new($"{Root}/.local/state/logs", 36_000_000, JunkCategory.SystemLogs, "User logs (synthetic)", true),
        new($"{Root}/.local/share/Trash", 2_050_000_000, JunkCategory.TrashBin, "Trash (synthetic)", true),
    ];

    public async IAsyncEnumerable<JunkItem> ScanAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IProgress<int>? filesScanned = null)
    {
        int count = 0;
        foreach (var item in Items)
        {
            // Long enough to watch the list grow; short enough not to slow anything down.
            await Task.Delay(150, cancellationToken);
            filesScanned?.Report(count += 1_000);
            yield return item;
        }
    }
}
#endif
