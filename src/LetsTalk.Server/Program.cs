using LetsTalk.Protocols;
using System.Threading.Tasks;

namespace LetsTalk.Server
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            var messageProtocol = new LengthProtocol();
            var messageProcessor = new MessageProcessor();
            var server = new Server(messageProtocol, messageProcessor);
            await server.StartAsync();
        }

    }
}
