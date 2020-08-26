using LetsTalk.Network;
using LetsTalk.Protocols;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;

namespace LetsTalk.Server
{
    public class DoSomethingWithEachClient
    {
        private readonly IMessageProtocol _messageProtocol;
        private Task _sendTask;
        private Task _receiveTask;

        public DoSomethingWithEachClient(IMessageProtocol messageProtocol)
        {
            _messageProtocol = messageProtocol;
        }

        public void AddConnection(SocketConnection socketConnection)
        {
            _ = Task.Run(() => socketConnection.StartAsync());
            _sendTask = StartSend(socketConnection.ApplicationWriter, socketConnection.ConnectionId);
            _receiveTask = StartReceive(socketConnection.ApplicationReader, socketConnection.ConnectionId);
        }

        private Task StartReceive(PipeReader reader, string id)
        {
            return Task.Run(async () =>
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
                        Console.WriteLine($"{id}: {readResult}");
                    }

                    if (result.IsCompleted)
                        break;

                    reader.AdvanceTo(consumed);
                }
            });
        }

        private Task StartSend(PipeWriter writer, string id)
        {
            return Task.Run(async () =>
            {
                while (true)
                {
                    var data = $"From the server: {id}";
                    var encoded = Encoding.UTF8.GetBytes(data);
                    var msg = new Message(encoded);

                    _messageProtocol.WriteMessage(msg, writer);
                    FlushResult result = await writer.FlushAsync();

                    if (result.IsCompleted)
                        break;

                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            });

        }
    }
}
