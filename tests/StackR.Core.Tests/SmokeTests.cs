
namespace StackR.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_Assembly_Loads()
    {
        Assert.NotNull(typeof(StackR.Core.StackRPaths).Assembly);
    }
}
