using LetsTalk.Network;
using LetsTalk.Protocols;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace LetsTalk.Client
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            IMessageProtocol messageProtocol = new LengthProtocol();
            Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            SocketConnection sc = new SocketConnection(socket);
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 8087));
            var connectionTask = sc.StartAsync();
          
            Application app = new Application(sc.ApplicationWriter, sc.ApplicationReader, messageProtocol);

            var appTask = app.StartAsync();
            var readTask = app.StartRecieve();

            await Task.WhenAny(connectionTask.AsTask(), appTask, readTask);
        }


    }

    public class Application
    {
        private readonly IMessageProtocol _messageProtocol;

        private readonly PipeWriter _pipeWriter;
        private readonly PipeReader _pipeReader;

        public Application(PipeWriter writer, PipeReader reader, IMessageProtocol messageProtocol)
        {
            _messageProtocol = messageProtocol;
            _pipeWriter = writer;
            _pipeReader = reader;
        }

        public async Task StartAsync()
        {
            int count = 1;
            while (true)
            {
                var data = $"From the Client: {count}";
                var encoded = Encoding.UTF8.GetBytes(data);
                var msg = new Message(encoded);

                _messageProtocol.WriteMessage(msg, _pipeWriter);
                FlushResult result = await _pipeWriter.FlushAsync();

                if (result.IsCompleted)
                    break;

                count++;
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        public async Task StartRecieve()
        {
            while(true)
            {
                ReadResult result = await _pipeReader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                SequencePosition consumed = buffer.Start;
                SequencePosition examined = buffer.Start;
                if (_messageProtocol.TryParseMessage(buffer, ref consumed, ref examined, out var msg))
                {
                    var readResult = Encoding.UTF8.GetString(msg.Payload.ToArray());
                    Console.WriteLine($"Client Msg: {readResult}");
                }

                if (result.IsCompleted)
                    break;

                _pipeReader.AdvanceTo(consumed);
            }
        }
    }
}
