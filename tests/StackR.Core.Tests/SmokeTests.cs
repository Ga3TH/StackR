using System.Diagnostics.CodeAnalysis;

namespace StackR.Core.Tests;

public class SmokeTests
{
    [Fact]
    [SuppressMessage("Naming", "CA1707", Justification = "Test names use the Method_Scenario_Expectation convention from the brief.")]
    public void Core_Assembly_Loads()
    {
        Assert.NotNull(typeof(StackR.Core.StackRPaths).Assembly);
    }
}
