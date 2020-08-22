using System.Buffers;

namespace LetsTalk.Protocols
{

    public struct Message
    {
        public Message(byte[] payload) : this(new ReadOnlySequence<byte>(payload)) { }

        public Message(ReadOnlySequence<byte> payload)
        {
            Payload = payload;
        }

        public ReadOnlySequence<byte> Payload { get; }
    }
}
