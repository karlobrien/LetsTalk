using LetsTalk.Network;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LetsTalk.Server
{
    public class ReadWriteServer
    {
        private readonly Socket _listenSocket;
        private readonly DoSomethingWithEachClient _serverApplication;

        public ReadWriteServer(DoSomethingWithEachClient serverApplication)
        {
            _serverApplication = serverApplication;

            _listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            _listenSocket.Bind(new IPEndPoint(IPAddress.Loopback, 8087));
            Console.WriteLine("Listening on port 8087");
            _listenSocket.Listen(120);
        }

        public async Task StartAsync()
        {
            while (true)
            {
                var clientSocket = await _listenSocket.AcceptAsync();
                Console.WriteLine("Connection Request Received:");
                SocketConnection sc = new SocketConnection(clientSocket);
                _serverApplication.AddConnection(sc);
            }
        }
    }
}
