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
}
