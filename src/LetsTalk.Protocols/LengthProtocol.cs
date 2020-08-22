using System;
using System.Buffers;
using System.Buffers.Binary;

namespace LetsTalk.Protocols
{
    public class LengthProtocol : IMessageProtocol
    {
        const int HeaderSize = 4;
        public void WriteMessage(Message message, IBufferWriter<byte> output)
        {
            var header = output.GetSpan(HeaderSize);
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)message.Payload.Length);
            output.Advance(HeaderSize);

            foreach (var msg in message.Payload)
                output.Write(msg.Span);
        }

        public bool TryParseMessage(in ReadOnlySequence<byte> input, ref SequencePosition consumed, ref SequencePosition examined, out Message message)
        {
            var reader = new SequenceReader<byte>(input);

            if (input.Length < HeaderSize)
            {
                message = default;
                return false;
            }

            //https://github.com/grpc/grpc-dotnet/blob/6504c26be7ff763f6367daf1a43b8c01eefc3e7a/src/Grpc.AspNetCore.Server/Internal/PipeExtensions.cs#L158-L184
            int length = 0;
            if (input.First.Length >= HeaderSize)
            {
                var header = input.First.Span.Slice(0, HeaderSize);
                length = BinaryPrimitives.ReadInt32BigEndian(header);
            }
            else
            {
                Span<byte> header = stackalloc byte[HeaderSize];
                input.Slice(0, HeaderSize).CopyTo(header);
                length = BinaryPrimitives.ReadInt32BigEndian(header);
            }

            //check here if we have enough to read the message otherwise try and read more data
            if (input.Length < HeaderSize + length)
            {
                message = default;
                return false;
            }

            //could have used input.slice(HeaderSize + length) here
            var t = input.Slice(HeaderSize, length);
            //var t = input.Slice(HeaderSize + length);
            //TODO: Do we go from a pool of preallocated messages here?
            message = new Message(t);

            consumed = t.End;
            examined = consumed;

            return true;
        }
    }
}
