using LetsTalk.Network;
using LetsTalk.Protocols;
using System;
using System.IO.Pipelines;
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

            Application app = new Application(pipe.Writer, messageProtocol);
            SocketConnection sc = new SocketConnection(pipe.Reader);
            var connectionTask = sc.StartAsync();
            var appTask = app.StartAsync();

            await Task.WhenAny(connectionTask.AsTask(), appTask);
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
                var data = $"Item: {count}";
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
    }
}
