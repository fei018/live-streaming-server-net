﻿namespace LiveStreamingServerNet.Transmuxer.Contracts
{
    public delegate Task OnTransmuxerStarted(string outputPath);
    public delegate Task OnTransmuxerEnded(string outputPath);

    public interface ITransmuxer
    {
        string Name { get; }
        Guid ContextIdentifier { get; }
        Task RunAsync(string inputPath, OnTransmuxerStarted? onStarted, OnTransmuxerEnded? onEnded, CancellationToken cancellation);
    }
}
