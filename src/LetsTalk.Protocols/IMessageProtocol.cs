using System;
using System.Buffers;
using System.Buffers.Binary;

namespace LetsTalk.Protocols
{

    public interface IMessageProtocol
    {
        bool TryParseMessage(in ReadOnlySequence<byte> input, ref SequencePosition consumed, ref SequencePosition examined, out Message message);
        void WriteMessage(Message message, IBufferWriter<byte> output);
    }
}
