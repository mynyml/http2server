using System;                 // Console
using System.Net;             // IPAddress, IPEndPoint
using System.Net.Sockets;     // TcpListener, Socket, NetworkStream
using System.Text;            // Encoding
using System.Threading.Tasks; // Task

namespace HTTP2
{
  class Server
  {
    private readonly TcpListener tcpServer;

    public Server(IPAddress address, int port)
    {
        this.tcpServer = new TcpListener(address, port);
        this.tcpServer.Start();
    }

    public async Task Listen()
    {
      while (true)
      {
        using (var socket = await this.tcpServer.AcceptSocketAsync())
        {
          Console.WriteLine("Request from {0}", (IPEndPoint)socket.RemoteEndPoint);

          var stream = new NetworkStream(socket);
          var recvBuffer = new byte[4096];

          while (true)
          {
            Array.Clear(recvBuffer, 0, recvBuffer.Length);
            await stream.ReadAsync(recvBuffer, 0, recvBuffer.Length);

            if (this.IsBufferEmpty(recvBuffer))
            {
              Console.WriteLine("Terminating connection");
              break;
            }

            await stream.WriteAsync(recvBuffer, 0, recvBuffer.Length);
            await stream.FlushAsync();
          }
        }
      }
    }

    private bool IsBufferEmpty(byte[] buffer)
    {
      String str;

      str = System.Text.Encoding.UTF8.GetString(buffer);
      str = str.TrimEnd('\0');

      return String.IsNullOrWhiteSpace(str);
    }
  }

  class Program
  {
    public static void Main()
    {
      var port   = 3000;
      var server = new Server(IPAddress.Any, port);

      var task = Task.Run(() => server.Listen());
      Console.WriteLine("http2 server listening on port {0}", port);

      task.Wait();
    }
  }
}