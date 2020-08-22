using LetsTalk.Protocols;
using System;
using System.IO;
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
            Pipe pipe = new Pipe();

            //var data = Console.ReadLine();
            //var encoded = Encoding.UTF8.GetBytes(data);
            //var msg = new Message(encoded);
            //messageProtocol.WriteMessage(msg, pipe.Writer);
            //pipe.Writer.Advance((int)msg.Payload.Length);
            //FlushResult result = await pipe.Writer.FlushAsync();
            Application app = new Application(pipe.Writer, messageProtocol);

            SocketConnection sc = new SocketConnection(pipe.Reader);
            var connectionTask = sc.StartAsync();
            var appTask = app.StartAsync();

            await Task.WhenAny(connectionTask.AsTask(), appTask);

            Console.WriteLine("Connecting to port 8087");
        }

        
    }

    public class Application
    {
        private readonly IMessageProtocol _messageProtocol;

        private readonly PipeWriter _pipeWriter;

        public Application(PipeWriter writer, IMessageProtocol messageProtocol)
        {
            _messageProtocol = messageProtocol;
            _pipeWriter = writer;
        }

        public async Task StartAsync()
        {
            int count = 1;
            while (true)
            {
                //Console.WriteLine("Enter some input: ");
                var data = $"Item: {count}";
                var encoded = Encoding.UTF8.GetBytes(data);
                var msg = new Message(encoded);

                _messageProtocol.WriteMessage(msg, _pipeWriter);
                //_pipeWriter.Advance((int)msg.Payload.Length);
                //_pipeWriter.Advance(encoded.Length);

                FlushResult result = await _pipeWriter.FlushAsync();

                if (result.IsCompleted)
                    break;

                count++;
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
