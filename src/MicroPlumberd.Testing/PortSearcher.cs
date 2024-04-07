using System.Net;
using System.Net.Sockets;

namespace MicroPlumberd.Testing;

public class PortSearcher
{
    private int lastPort = 2700;

    public PortSearcher()
    {
        
    }

    public PortSearcher(int startPort)
    {
        lastPort = startPort;
    }
    public int FindNextAvailablePort()
    {
        var actual = Interlocked.Increment(ref this.lastPort);
        while (true)
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                
                socket.Bind(new IPEndPoint(IPAddress.Loopback, actual));
                socket.Listen();
                socket.Close();
                socket.Dispose();
                Thread.Sleep(100);
                
                return actual;
            }
            catch (SocketException)
            {
                // Increment the port number and try again
                actual = Interlocked.Increment(ref this.lastPort);
                socket.Dispose();
            }
        }
    }
}