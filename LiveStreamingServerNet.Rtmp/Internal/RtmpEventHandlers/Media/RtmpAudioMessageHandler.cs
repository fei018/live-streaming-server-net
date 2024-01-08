﻿using LiveStreamingServerNet.Newtorking.Contracts;
using LiveStreamingServerNet.Rtmp.Configurations;
using LiveStreamingServerNet.Rtmp.Internal.Contracts;
using LiveStreamingServerNet.Rtmp.Internal.RtmpEventHandlers.MessageDispatcher.Attributes;
using LiveStreamingServerNet.Rtmp.Internal.RtmpEventHandlers.MessageDispatcher.Contracts;
using LiveStreamingServerNet.Rtmp.Internal.Services.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LiveStreamingServerNet.Rtmp.Internal.RtmpEventHandlers.Media
{
    [RtmpMessageType(RtmpMessageType.AudioMessage)]
    internal class RtmpAudioMessageHandler : IRtmpMessageHandler
    {
        private readonly IRtmpStreamManagerService _streamManager;
        private readonly IRtmpMediaMessageManagerService _mediaMessageManager;
        private readonly RtmpServerConfiguration _config;
        private readonly ILogger _logger;

        public RtmpAudioMessageHandler(
            IRtmpStreamManagerService streamManager,
            IRtmpMediaMessageManagerService mediaMessageManager,
            IOptions<RtmpServerConfiguration> config,
            ILogger<RtmpAudioMessageHandler> logger)
        {
            _streamManager = streamManager;
            _mediaMessageManager = mediaMessageManager;
            _config = config.Value;
            _logger = logger;
        }

        public Task<bool> HandleAsync(
            IRtmpChunkStreamContext chunkStreamContext,
            IRtmpClientContext clientContext,
            INetBuffer payloadBuffer,
            CancellationToken cancellationToken)
        {
            var publishStreamContext = clientContext.PublishStreamContext ??
                throw new InvalidOperationException("Stream is not yet published.");

            var hasHeader = CacheAudioSequence(chunkStreamContext, publishStreamContext, payloadBuffer);
            BroacastAudioMessageToSubscribers(chunkStreamContext, publishStreamContext, payloadBuffer, hasHeader);
            return Task.FromResult(true);
        }

        private void BroacastAudioMessageToSubscribers(
            IRtmpChunkStreamContext chunkStreamContext,
            IRtmpPublishStreamContext publishStreamContext,
            INetBuffer payloadBuffer,
            bool hasSequenceHeader)
        {
            if (hasSequenceHeader)
            {
                using var subscribers = _streamManager.GetSubscribersLocked(publishStreamContext.StreamPath);
                BroacastAudioMessageToSubscribers(chunkStreamContext, false, payloadBuffer, subscribers.Value);
            }
            else
            {
                var subscribers = _streamManager.GetSubscribers(publishStreamContext.StreamPath);
                BroacastAudioMessageToSubscribers(chunkStreamContext, true, payloadBuffer, subscribers);
            }
        }

        private void BroacastAudioMessageToSubscribers(
            IRtmpChunkStreamContext chunkStreamContext,
            bool isSkippable,
            INetBuffer payloadBuffer,
            IList<IRtmpClientContext> subscribers)
        {
            _mediaMessageManager.EnqueueMediaMessage(
                subscribers,
                MediaType.Audio,
                chunkStreamContext.MessageHeader.Timestamp,
                chunkStreamContext.MessageHeader.MessageStreamId,
                isSkippable,
                payloadBuffer.CopyAllTo);
        }

        private bool CacheAudioSequence(
            IRtmpChunkStreamContext chunkStreamContext,
            IRtmpPublishStreamContext publishStreamContext,
            INetBuffer payloadBuffer)
        {
            var firstByte = payloadBuffer.ReadByte();
            var soundFormat = (AudioSoundFormat)(firstByte >> 4);

            if (soundFormat == AudioSoundFormat.AAC)
            {
                var aacPackageType = (AACPacketType)payloadBuffer.ReadByte();
                if (aacPackageType == AACPacketType.SequenceHeader)
                {
                    _mediaMessageManager.CacheSequenceHeader(publishStreamContext, MediaType.Audio, payloadBuffer);
                    return true;
                }
                else if (_config.EnableGopCaching)
                {
                    _mediaMessageManager.CachePicture(publishStreamContext, MediaType.Audio, payloadBuffer, chunkStreamContext.MessageHeader.Timestamp);
                }
            }

            payloadBuffer.MoveTo(0);
            return false;
        }
    }
}
