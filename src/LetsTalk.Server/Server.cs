using LetsTalk.Protocols;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace LetsTalk.Server
{

    public class Server
    {
        private readonly Socket _listenSocket;
        private readonly IMessageProtocol _messageProtocol;
        private readonly MessageProcessor _messageProcessor;

        public Server(IMessageProtocol messageProtocol, MessageProcessor messageProcessor)
        {
            _messageProtocol = messageProtocol;
            _messageProcessor = messageProcessor;

            _listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            _listenSocket.Bind(new IPEndPoint(IPAddress.Loopback, 8087));
            Console.WriteLine("Listening on port 8087");
            _listenSocket.Listen(120);
        }

        public async Task StartAsync()
        {
            while(true)
            {
                var clientSocket = await _listenSocket.AcceptAsync();
                _ = ReadPipeAsync(clientSocket);
            }
        }

        private async Task ReadPipeAsync(Socket socket)
        {
            Console.WriteLine($"[{socket.RemoteEndPoint}]: connected");

            // Create a PipeReader over the network stream
            var stream = new NetworkStream(socket);
            var reader = PipeReader.Create(stream);
    
            while (true)
            {
                ReadResult result = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                SequencePosition consumed = buffer.Start;
                SequencePosition examined = buffer.Start;
                if (_messageProtocol.TryParseMessage(buffer, ref consumed, ref examined, out var msg))
                {
                    _messageProcessor.ProcessMessage(msg);
                }

                if (result.IsCompleted)
                    break;

                reader.AdvanceTo(consumed);
            }
        }

    }
}
