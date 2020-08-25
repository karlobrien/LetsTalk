using LetsTalk.Network;
using LetsTalk.Protocols;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace LetsTalk.Server
{
    public class ReadWriteServer
    {
        private readonly Socket _listenSocket;
        private readonly IMessageProtocol _messageProtocol;
        private readonly MessageProcessor _messageProcessor;


        public ReadWriteServer(IMessageProtocol messageProtocol, MessageProcessor messageProcessor)
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
            while (true)
            {
                var clientSocket = await _listenSocket.AcceptAsync();

                //new create or borrow a connection
                //add to the overall list of connections

                SocketConnection sc = new SocketConnection(clientSocket);
                var startTask = sc.StartAsync();
                var respondTask = RespondToClient(sc.ApplicationWriter);
                var clientTask = RecieveClient(sc.ApplicationReader);

                await Task.WhenAll(startTask.AsTask(), respondTask, clientTask);
            }
        }

        public async Task RecieveClient(PipeReader reader)
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                SequencePosition consumed = buffer.Start;
                SequencePosition examined = buffer.Start;
                if (_messageProtocol.TryParseMessage(buffer, ref consumed, ref examined, out var msg))
                {
                    var readResult = Encoding.UTF8.GetString(msg.Payload.ToArray());
                    Console.WriteLine($"{readResult}");
                }

                if (result.IsCompleted)
                    break;

                reader.AdvanceTo(consumed);
            }
        }

        private async Task RespondToClient(PipeWriter writer)
        {
            int count = 1;
            while (true)
            {
                var data = $"From the server: {count}";
                var encoded = Encoding.UTF8.GetBytes(data);
                var msg = new Message(encoded);

                _messageProtocol.WriteMessage(msg, writer);
                FlushResult result = await writer.FlushAsync();

                if (result.IsCompleted)
                    break;

                count++;
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }
}
