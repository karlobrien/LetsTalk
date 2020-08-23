using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LetsTalk.Network
{
    public class SocketConnection
    {
        private volatile bool _aborted;
        private readonly Socket _socket;
        private readonly SocketSender _sender;
        private readonly SocketReceiver _receiver;
        private PipeReader _reader;
        private PipeWriter _writer;

        public SocketConnection(PipeReader reader, PipeWriter writer)
        {
            _socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            _sender = new SocketSender(_socket, PipeScheduler.ThreadPool);
            _receiver = new SocketReceiver(_socket, PipeScheduler.ThreadPool);

            _reader = reader;
            _writer = writer;
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
                    var receiveTask = DoReceive();
                    var sendTask = DoSend();

                    // If the sending task completes then close the receive
                    // We don't need to do this in the other direction because the kestrel
                    // will trigger the output closing once the input is complete.
                    if (await Task.WhenAny(receiveTask, sendTask).ConfigureAwait(false) == sendTask)
                    {
                        // Tell the reader it's being aborted
                        _socket.Dispose();
                    }

                    // Now wait for both to complete
                    await receiveTask;
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

        private async Task DoReceive()
        {
            Exception error = null;

            try
            {
                await ProcessReceives().ConfigureAwait(false);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                error = new Exception(ex.Message, ex);
                //error = new ConnectionResetException(ex.Message, ex);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted ||
                                             ex.SocketErrorCode == SocketError.ConnectionAborted ||
                                             ex.SocketErrorCode == SocketError.Interrupted ||
                                             ex.SocketErrorCode == SocketError.InvalidArgument)
            {
                if (!_aborted)
                {
                    // Calling Dispose after ReceiveAsync can cause an "InvalidArgument" error on *nix.
                    error = new Exception();
                    //error = new ConnectionAbortedException();
                }
            }
            catch (ObjectDisposedException)
            {
                if (!_aborted)
                {
                    error = new Exception();
                    //error = new ConnectionAbortedException();
                }
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
                if (_aborted)
                {
                    error ??= new Exception();
                    //error ??= new ConnectionAbortedException();
                }

                await _writer.CompleteAsync(error).ConfigureAwait(false);
            }
        }

        private async Task ProcessReceives()
        {
            while (true)
            {
                // Ensure we have some reasonable amount of buffer space
                var buffer = _writer.GetMemory();

                var bytesReceived = await _receiver.ReceiveAsync(buffer);

                if (bytesReceived == 0)
                {
                    // FIN
                    break;
                }

                _writer.Advance(bytesReceived);

                var flushTask = _writer.FlushAsync();

                if (!flushTask.IsCompleted)
                {
                    await flushTask.ConfigureAwait(false);
                }

                var result = flushTask.Result;
                if (result.IsCompleted)
                {
                    // Pipe consumer is shut down, do we stop writing
                    break;
                }
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
                _aborted = true;
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
}
