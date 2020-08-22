using LetsTalk.Protocols;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LetsTalk.Client
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            IMessageProtocol messageProtocol = new LengthProtocol();
            Pipe pipe = new Pipe();

            var data = Console.ReadLine();
            var encoded = Encoding.UTF8.GetBytes(data);
            var msg = new Message(encoded);

            messageProtocol.WriteMessage(msg, pipe.Writer);
            pipe.Writer.Advance((int)msg.Payload.Length);

            FlushResult result = await pipe.Writer.FlushAsync();

            SocketConnection sc = new SocketConnection(pipe.Reader);
            await sc.StartAsync();

            Console.WriteLine("Connecting to port 8087");
        }

        
    }

    public class SocketConnection
    {
        private readonly Socket _socket;
        private readonly SocketSender _sender;
        private PipeReader _reader;

        public SocketConnection(PipeReader reader)
        {
            _socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            _sender = new SocketSender(_socket, PipeScheduler.ThreadPool);

            //read from this and send
            _reader = reader;
        }

        public async ValueTask StartAsync()
        {
            try
            {
                await _socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 8087));
                await ExecuteAsync();
            }
            catch (Exception)
            {

                throw;
            }
        }

        private async Task ExecuteAsync()
        {
            try
            {
                Exception sendError = null;
                try
                {
                    // Spawn send and receive logic
                    //var receiveTask = DoReceive();
                    var sendTask = DoSend();

                    // If the sending task completes then close the receive
                    // We don't need to do this in the other direction because the kestrel
                    // will trigger the output closing once the input is complete.
                    if (await Task.WhenAny(sendTask).ConfigureAwait(false) == sendTask)
                    {
                        // Tell the reader it's being aborted
                        _socket.Dispose();
                    }

                    // Now wait for both to complete
                    //await receiveTask;
                    sendError = await sendTask;

                    // Dispose the socket(should noop if already called)
                    _socket.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected exception in {nameof(SocketConnection)}.{nameof(StartAsync)}: " + ex);
                }
                finally
                {
                    // Complete the output after disposing the socket
                    _reader.Complete(sendError);
                }
            }
            catch (Exception)
            {

                throw;
            }
        }

        private async Task<Exception> DoSend()
        {
            Exception error = null;

            try
            {
                await ProcessSends().ConfigureAwait(false);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                error = null;
            }
            catch (ObjectDisposedException)
            {
                error = null;
            }
            catch (IOException ex)
            {
                error = ex;
            }
            catch (Exception ex)
            {
                error = new IOException(ex.Message, ex);
            }
            finally
            {
                //_aborted = true;
                _socket.Shutdown(SocketShutdown.Both);
            }

            return error;
        }

        private async Task ProcessSends()
        {
            while (true)
            {
                // Wait for data to write from the pipe producer
                var result = await _reader.ReadAsync().ConfigureAwait(false);
                var buffer = result.Buffer;

                if (result.IsCanceled)
                {
                    break;
                }

                var end = buffer.End;
                var isCompleted = result.IsCompleted;
                if (!buffer.IsEmpty)
                {
                    await _sender.SendAsync(buffer);
                }

                _reader.AdvanceTo(end);

                if (isCompleted)
                {
                    break;
                }
            }
        }

    }

    internal class SocketSender
    {
        private readonly Socket _socket;
        private readonly SocketAsyncEventArgs _eventArgs = new SocketAsyncEventArgs();
        private readonly SocketAwaitable _awaitable;

        private List<ArraySegment<byte>> _bufferList;

        public SocketSender(Socket socket, PipeScheduler scheduler)
        {
            _socket = socket;
            _awaitable = new SocketAwaitable(scheduler);
            _eventArgs.UserToken = _awaitable;
            _eventArgs.Completed += (_, e) => ((SocketAwaitable)e.UserToken).Complete(e.BytesTransferred, e.SocketError);
        }

        public SocketAwaitable SendAsync(in ReadOnlySequence<byte> buffers)
        {
            if (buffers.IsSingleSegment)
            {
                return SendAsync(buffers.First);
            }

#if NETCOREAPP
            if (!_eventArgs.MemoryBuffer.Equals(Memory<byte>.Empty))
#else
            if (_eventArgs.Buffer != null)
#endif
            {
                _eventArgs.SetBuffer(null, 0, 0);
            }

            _eventArgs.BufferList = GetBufferList(buffers);

            if (!_socket.SendAsync(_eventArgs))
            {
                _awaitable.Complete(_eventArgs.BytesTransferred, _eventArgs.SocketError);
            }

            return _awaitable;
        }

        private SocketAwaitable SendAsync(ReadOnlyMemory<byte> memory)
        {
            // The BufferList getter is much less expensive then the setter.
            if (_eventArgs.BufferList != null)
            {
                _eventArgs.BufferList = null;
            }

#if NETCOREAPP
            _eventArgs.SetBuffer(MemoryMarshal.AsMemory(memory));
#else
            var segment = memory.GetArray();

            _eventArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);
#endif
            if (!_socket.SendAsync(_eventArgs))
            {
                _awaitable.Complete(_eventArgs.BytesTransferred, _eventArgs.SocketError);
            }

            return _awaitable;
        }

        private List<ArraySegment<byte>> GetBufferList(in ReadOnlySequence<byte> buffer)
        {
            Debug.Assert(!buffer.IsEmpty);
            Debug.Assert(!buffer.IsSingleSegment);

            if (_bufferList == null)
            {
                _bufferList = new List<ArraySegment<byte>>();
            }
            else
            {
                // Buffers are pooled, so it's OK to root them until the next multi-buffer write.
                _bufferList.Clear();
            }

            foreach (var b in buffer)
            {
                _bufferList.Add(b.GetArray());
            }

            return _bufferList;
        }
    }

    internal class SocketAwaitable : ICriticalNotifyCompletion
    {
        private static readonly Action _callbackCompleted = () => { };

        private readonly PipeScheduler _ioScheduler;

        private Action _callback;
        private int _bytesTransferred;
        private SocketError _error;

        public SocketAwaitable(PipeScheduler ioScheduler)
        {
            _ioScheduler = ioScheduler;
        }

        public SocketAwaitable GetAwaiter() => this;
        public bool IsCompleted => ReferenceEquals(_callback, _callbackCompleted);

        public int GetResult()
        {
            Debug.Assert(ReferenceEquals(_callback, _callbackCompleted));

            _callback = null;

            if (_error != SocketError.Success)
            {
                throw new SocketException((int)_error);
            }

            return _bytesTransferred;
        }

        public void OnCompleted(Action continuation)
        {
            if (ReferenceEquals(_callback, _callbackCompleted) ||
                ReferenceEquals(Interlocked.CompareExchange(ref _callback, continuation, null), _callbackCompleted))
            {
                Task.Run(continuation);
            }
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }

        public void Complete(int bytesTransferred, SocketError socketError)
        {
            _error = socketError;
            _bytesTransferred = bytesTransferred;
            var continuation = Interlocked.Exchange(ref _callback, _callbackCompleted);

            if (continuation != null)
            {
                _ioScheduler.Schedule(state => ((Action)state)(), continuation);
            }
        }
    }

    internal static class BufferExtensions
    {
        public static ArraySegment<byte> GetArray(this Memory<byte> memory)
        {
            return ((ReadOnlyMemory<byte>)memory).GetArray();
        }

        public static ArraySegment<byte> GetArray(this ReadOnlyMemory<byte> memory)
        {
            if (!MemoryMarshal.TryGetArray(memory, out var result))
            {
                throw new InvalidOperationException("Buffer backed by array was expected");
            }
            return result;
        }
    }
}
