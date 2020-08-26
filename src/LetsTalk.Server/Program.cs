using LetsTalk.Protocols;
using System.Threading.Tasks;

namespace LetsTalk.Server
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            var messageProtocol = new LengthProtocol();
            DoSomethingWithEachClient sa = new DoSomethingWithEachClient(messageProtocol);
            ReadWriteServer server = new ReadWriteServer(sa);
            await server.StartAsync();
        }

    }
}
