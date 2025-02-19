﻿using LiveStreamingServerNet.Flv.Internal.Contracts;
using LiveStreamingServerNet.Utilities.Contracts;

namespace LiveStreamingServerNet.Flv.Internal.Services.Contracts
{
    internal interface IFlvStreamManagerService
    {
        bool IsStreamPathPublishing(string streamPath, bool requireReady = true);

        PublishingStreamResult StartPublishingStream(IFlvStreamContext streamContext);
        bool StopPublishingStream(string streamPath, out IList<IFlvClient> existingSubscribers);
        IFlvStreamContext? GetFlvStreamContext(string streamPath);

        SubscribingStreamResult StartSubscribingStream(IFlvClient client, string streamPath);
        bool StopSubscribingStream(IFlvClient client);
        IRentable<IReadOnlyList<IFlvClient>> GetSubscribersLocked(string streamPath);
        IReadOnlyList<IFlvClient> GetSubscribers(string streamPath);
    }

    internal enum PublishingStreamResult
    {
        Succeeded,
        AlreadyExists
    }

    internal enum SubscribingStreamResult
    {
        Succeeded,
        StreamDoesntExist,
        AlreadySubscribing,
    }
}
