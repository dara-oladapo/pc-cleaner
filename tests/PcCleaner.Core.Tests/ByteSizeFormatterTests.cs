using PcCleaner.Core.Utilities;

namespace PcCleaner.Core.Tests;

public class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024 * 1024, "1.0 MB")]
    [InlineData(1024L * 1024 * 1024, "1.0 GB")]
    [InlineData(1024L * 1024 * 1024 * 1024, "1.0 TB")]
    public void Format_ProducesExpectedString(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
    }

    [Fact]
    public void Format_NegativeBytes_IsPrefixedWithMinus()
    {
        Assert.Equal("-1.0 KB", ByteSizeFormatter.Format(-1024));
    }
}
