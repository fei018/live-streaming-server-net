﻿using LiveStreamingServerNet.Networking.Configurations;
using LiveStreamingServerNet.Networking.Contracts;
using LiveStreamingServerNet.Networking.Exceptions;
using LiveStreamingServerNet.Networking.Logging;
using LiveStreamingServerNet.Utilities.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Channels;

namespace LiveStreamingServerNet.Networking
{
    internal sealed class Client : IClient
    {
        private readonly INetBufferPool _netBufferPool;
        private readonly SecurityConfiguration _config;
        private readonly ILogger _logger;
        private readonly Channel<PendingMessage> _pendingMessageChannel;
        private TcpClient _tcpClient = default!;
        private CancellationTokenSource? _cts;
        private TaskCompletionSource _stoppedTcs = new();

        public uint ClientId { get; }

        public Client(uint clientId, TcpClient tcpClient, INetBufferPool netBufferPool, IOptions<SecurityConfiguration> config, ILogger<Client> logger)
        {
            ClientId = clientId;
            _tcpClient = tcpClient;
            _netBufferPool = netBufferPool;
            _config = config.Value;
            _logger = logger;
            _pendingMessageChannel = Channel.CreateUnbounded<PendingMessage>();
        }

        public bool IsConnected => _tcpClient?.Connected ?? false;

        public EndPoint LocalEndPoint => _tcpClient.Client.LocalEndPoint!;
        public EndPoint RemoteEndPoint => _tcpClient.Client.RemoteEndPoint!;

        public async Task RunAsync(IClientHandler handler, ServerEndPoint serverEndPoint, CancellationToken stoppingToken)
        {
            _logger.ClientConnected(ClientId);

            await handler.InitializeAsync(this);

            Stream? networkStream = null;
            await using (var outstandingBufferSender = new OutstandingBufferSender(ClientId, _pendingMessageChannel.Reader, _logger))
            {
                _cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var cancellationToken = _cts.Token;

                try
                {
                    networkStream = await CreateNetworkStreamAsync(serverEndPoint);
                    outstandingBufferSender.Start(networkStream, cancellationToken);

                    var readOnlyNetworkStream = new ReadOnlyStream(networkStream);
                    while (_tcpClient.Connected && !cancellationToken.IsCancellationRequested)
                        if (!await handler.HandleClientLoopAsync(readOnlyNetworkStream, cancellationToken))
                            break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                catch (Exception ex) when (ex is IOException or EndOfStreamException) { }
                catch (Exception ex)
                {
                    _logger.ClientLoopError(ClientId, ex);
                }
                finally
                {
                    _cts.Cancel();
                }
            }

            await handler.DisposeAsync();
            networkStream?.Dispose();
            _tcpClient.Close();

            _logger.ClientDisconnected(ClientId);
        }

        public void Send(INetBuffer netBuffer, Action<bool>? callback)
        {
            if (!IsConnected) return;

            var rentedBuffer = ArrayPool<byte>.Shared.Rent(netBuffer.Size);

            try
            {
                var originalPosition = netBuffer.Position;
                netBuffer.MoveTo(0).ReadBytes(rentedBuffer, 0, netBuffer.Size);
                netBuffer.MoveTo(originalPosition);

                if (!_pendingMessageChannel.Writer.TryWrite(new PendingMessage(rentedBuffer, netBuffer.Size, callback)))
                {
                    throw new Exception("Failed to write to the send channel");
                }
            }
            catch (Exception)
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
                throw;
            }
        }

        public void Send(Action<INetBuffer> writer, Action<bool>? callback)
        {
            if (!IsConnected) return;

            using var netBuffer = ObtainNetBuffer();
            writer.Invoke(netBuffer);

            var rentedBuffer = ArrayPool<byte>.Shared.Rent(netBuffer.Size);

            try
            {
                netBuffer.MoveTo(0).ReadBytes(rentedBuffer, 0, netBuffer.Size);

                if (!_pendingMessageChannel.Writer.TryWrite(new PendingMessage(rentedBuffer, netBuffer.Size, callback)))
                {
                    throw new Exception("Failed to write to the send channel");
                }
            }
            catch (Exception)
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
                throw;
            }
        }

        public Task SendAsync(INetBuffer netBuffer)
        {
            var tcs = new TaskCompletionSource();
            Send(netBuffer, SetResult);
            return tcs.Task;

            void SetResult(bool successful)
            {
                if (successful)
                    tcs.SetResult();
                else
                    tcs.SetException(new BufferSendingException());
            }
        }

        public Task SendAsync(Action<INetBuffer> writer)
        {
            var tcs = new TaskCompletionSource();
            Send(writer, SetResult);
            return tcs.Task;

            void SetResult(bool successful)
            {
                if (successful)
                    tcs.SetResult();
                else
                    tcs.SetException(new BufferSendingException());
            }
        }

        private INetBuffer ObtainNetBuffer()
        {
            return _netBufferPool.Obtain();
        }

        public void Disconnect()
        {
            _cts?.Cancel();
        }

        public async Task DisconnectAsync(CancellationToken cancellation)
        {
            Disconnect();
            await _stoppedTcs.Task.WithCancellation(cancellation);
        }

        public ValueTask DisposeAsync()
        {
            _tcpClient.Dispose();
            _stoppedTcs.TrySetResult();
            return ValueTask.CompletedTask;
        }

        private async Task<Stream> CreateNetworkStreamAsync(ServerEndPoint serverEndPoint)
        {
            if (serverEndPoint.IsSecure && _config.ServerCertificate != null)
            {
                var sslStream = new SslStream(_tcpClient.GetStream(), false);

                await sslStream.AuthenticateAsServerAsync(
                    _config.ServerCertificate,
                    false,
                    _config.SslProtocols,
                    _config.CheckCertificateRevocation);

                return sslStream;
            }

            return _tcpClient.GetStream();
        }

        private record struct PendingMessage(byte[] RentedBuffer, int BufferSize, Action<bool>? Callback);

        private class OutstandingBufferSender : IAsyncDisposable
        {
            private readonly uint _clientId;
            private readonly ChannelReader<PendingMessage> _pendingMessageReader;
            private readonly ILogger _logger;

            private Task? _task;

            public OutstandingBufferSender(uint clientId, ChannelReader<PendingMessage> pendingMessageReader, ILogger logger)
            {
                _clientId = clientId;
                _pendingMessageReader = pendingMessageReader;
                _logger = logger;
            }

            public void Start(Stream networkStream, CancellationToken cancellationToken)
            {
                _task = Task.Run(() => SendOutstandingBuffersAsync(networkStream, cancellationToken), cancellationToken);
            }

            private async Task SendOutstandingBuffersAsync(Stream networkStream, CancellationToken cancellationToken)
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var (rentedBuffer, bufferSize, callback) = await _pendingMessageReader.ReadAsync(cancellationToken);

                        try
                        {
                            await networkStream.WriteAsync(rentedBuffer, 0, bufferSize, cancellationToken);
                            InvokeCallback(callback, true);
                        }
                        catch (Exception ex)
                        when (ex is IOException || (ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
                        {
                            InvokeCallback(callback, false);
                        }
                        catch (Exception ex)
                        {
                            InvokeCallback(callback, false);
                            _logger.SendDataError(_clientId, ex);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(rentedBuffer);
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }

                while (_pendingMessageReader.TryRead(out var pendingMessage))
                {
                    InvokeCallback(pendingMessage.Callback, false);
                    ArrayPool<byte>.Shared.Return(pendingMessage.RentedBuffer);
                }

                void InvokeCallback(Action<bool>? callback, bool successful)
                {
                    try
                    {
                        callback?.Invoke(true);
                    }
                    catch { }
                }
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (_task != null)
                        await _task;
                }
                catch (Exception ex)
                {
                    _logger.OutstandingBufferSenderDisposeError(_clientId, ex);
                }
            }
        }
    }
}
