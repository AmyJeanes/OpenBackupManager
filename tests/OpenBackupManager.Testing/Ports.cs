using System.Net;
using System.Net.Sockets;

namespace OpenBackupManager.Testing;

public static class Ports
{
    // One free just now on this computer's loopback, for a listener that can't pick its own, as HttpListener can't
    public static int Free()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }
}
