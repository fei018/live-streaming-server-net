﻿using LiveStreamingServerNet.Networking.Contracts;
using LiveStreamingServerNet.Rtmp.Internal.Contracts;

namespace LiveStreamingServerNet.Rtmp.Internal.Services.Contracts
{
    internal interface IRtmpMediaMessageBroadcasterService
    {
        ValueTask BroadcastMediaMessageAsync(
            IRtmpPublishStreamContext publishStreamContext,
            IReadOnlyList<IRtmpClientContext> subscribers,
            MediaType mediaType,
            uint timestamp,
            bool isSkippable,
            INetBuffer payloadBuffer);

        void RegisterClient(IRtmpClientContext clientContext);
        void UnregisterClient(IRtmpClientContext clientContext);
    }
}
