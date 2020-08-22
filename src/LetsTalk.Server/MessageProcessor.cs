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

    public class MessageProcessor
    {
        public void ProcessMessage(Message msg)
        {
            var readResult = Encoding.UTF8.GetString(msg.Payload.ToArray());
            Console.WriteLine(readResult);
        }
    }
}
