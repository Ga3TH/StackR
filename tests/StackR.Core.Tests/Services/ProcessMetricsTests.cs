using System.Diagnostics;
using StackR.Core.Services;

namespace StackR.Core.Tests.Services;

public class ProcessMetricsTests
{
    [Fact]
    public void TryRead_CurrentProcess_HasMemoryAndStartTime()
    {
        using var me = Process.GetCurrentProcess();
        var snapshot = ProcessMetrics.TryRead(me.Id);
        Assert.NotNull(snapshot);
        Assert.True(snapshot.WorkingSetBytes > 0);
        Assert.True(snapshot.StartedAt <= DateTimeOffset.Now);
        Assert.True(snapshot.ProcessCount >= 1);
    }

    [Fact]
    public void TryRead_UnknownPid_IsNull() => Assert.Null(ProcessMetrics.TryRead(int.MaxValue - 7));

    [Fact]
    public void Descendants_WalksTreeAndIgnoresCycles()
    {
        var parentOf = new Dictionary<int, int> { [2] = 1, [3] = 2, [4] = 1, [5] = 9, [6] = 6, [7] = 8, [8] = 7 };
        Assert.Equal([2, 4, 3], ProcessMetrics.Descendants(1, parentOf));
        Assert.Empty(ProcessMetrics.Descendants(6, parentOf));
        Assert.Equal([8], ProcessMetrics.Descendants(7, parentOf));
    }
}
