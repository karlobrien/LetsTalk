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
    public class Program
    {
        static async Task Main(string[] args)
        {
            var listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            listenSocket.Bind(new IPEndPoint(IPAddress.Loopback, 8087));
            Console.WriteLine("Listening on port 8087");
            listenSocket.Listen(120);

            while (true)
            {
                var socket = await listenSocket.AcceptAsync();
                _ = ReadPipeAsync(socket);
            }
        }

        private static async Task ReadPipeAsync(Socket socket)
        {
            Console.WriteLine($"[{socket.RemoteEndPoint}]: connected");

            // Create a PipeReader over the network stream
            var stream = new NetworkStream(socket);
            var reader = PipeReader.Create(stream);

            IMessageProtocol messageProtocol = new LengthProtocol();
            while (true)
            {
                ReadResult result = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                SequencePosition consumed = buffer.Start;
                SequencePosition examined = buffer.Start;
                if (messageProtocol.TryParseMessage(buffer, ref consumed, ref examined, out var msg))
                {
                    ProcessMessage(msg);
                }

                if (result.IsCompleted)
                    break;

                reader.AdvanceTo(consumed);
            }

        }

        private static void ProcessMessage(Message msg)
        {
            var readResult = Encoding.UTF8.GetString(msg.Payload.ToArray());
            Console.WriteLine(readResult);
        }
    }
}
