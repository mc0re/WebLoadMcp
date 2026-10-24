using Microsoft.Extensions.Logging;
using WebLoadMcp.Logging;

namespace WebLoadMcp.Tests;

public class FileLoggerProviderTests
{
    [Fact]
    public void AppendsMessagesToFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"webload-{Guid.NewGuid():N}", "log.txt");
        try
        {
            using (var provider = new FileLoggerProvider(path))
            {
                var logger = provider.CreateLogger("Test.Category");
                logger.LogInformation("first {Value}", 1);
                logger.LogWarning(new InvalidOperationException("boom"), "second");
            }

            using (var provider = new FileLoggerProvider(path))
            {
                provider.CreateLogger("Test.Category").LogError("third");
            }

            var lines = File.ReadAllLines(path);
            Assert.EndsWith("[INF] Test.Category: first 1", lines[0]);
            Assert.EndsWith("[WRN] Test.Category: second", lines[1]);
            Assert.Contains(lines, l => l.Contains("InvalidOperationException: boom"));
            Assert.EndsWith("[ERR] Test.Category: third", lines[^1]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
