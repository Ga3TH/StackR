using System.Net;
using System.Net.Sockets;
using StackR.Core.Services;

namespace StackR.Core.Tests.Services;

public sealed class PortProbeTests
{
    [Fact]
    public void PortProbe_FindsListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var owner = PortProbe.FindOwner(port);

            Assert.NotNull(owner);
            Assert.Equal(Environment.ProcessId, owner.Pid);
            Assert.Equal(Path.GetFileName(Environment.ProcessPath), owner.ProcessName);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void FindOwner_FreePort_ReturnsNull()
    {
        var port = ServiceTestEnvironment.FreePorts(1)[0];

        Assert.Null(PortProbe.FindOwner(port));
    }

    [Fact]
    public void FindOwner_WildcardListener_IsFound()
    {
        var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            Assert.Equal(Environment.ProcessId, PortProbe.FindOwner(port)?.Pid);
        }
        finally
        {
            listener.Stop();
        }
    }
}
