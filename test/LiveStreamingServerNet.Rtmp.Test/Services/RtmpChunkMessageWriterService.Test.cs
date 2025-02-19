﻿using FluentAssertions;
using LiveStreamingServerNet.Networking;
using LiveStreamingServerNet.Rtmp.Internal.RtmpHeaders;
using LiveStreamingServerNet.Rtmp.Internal.Services;
using LiveStreamingServerNet.Rtmp.Test.Utilities;
using System.Security.Cryptography;

namespace LiveStreamingServerNet.Rtmp.Test.Services
{
    public class RtmpChunkMessageWriterServiceTest
    {
        [Theory]
        [InlineData(0, 0, 128)]
        [InlineData(0, 1024, 128)]
        [InlineData(0xf, 12345, 321)]
        [InlineData(0xfff, 128, 512)]
        [InlineData(0xffffff, 1024, 128)]
        [InlineData(0xfffffff, 1024, 128)]
        public async Task Write_Should_SeparateChunkType0MessageIntoChunks(uint timestamp, int payloadSize, int chunkSize)
        {
            // Arrange
            var expectedChunkStreamId = Helpers.CreateRandomChunkStreamId();
            var expectedTimestamp = timestamp;
            var expectedMessageTypeId = (byte)Random.Shared.Next();
            var expectedMessageStreamId = (uint)Random.Shared.Next();
            var expectedPayload = RandomNumberGenerator.GetBytes(payloadSize);
            var expectedChunkSize = chunkSize;

            using var payloadBuffer = new NetBuffer();
            payloadBuffer.Write(expectedPayload, 0, expectedPayload.Length);

            var basicHeader = new RtmpChunkBasicHeader(0, expectedChunkStreamId);
            var messageHeader = new RtmpChunkMessageHeaderType0(expectedTimestamp, expectedPayload.Length, expectedMessageTypeId, expectedMessageStreamId);

            using var streamBuffer = new NetBuffer();
            var service = new RtmpChunkMessageWriterService();

            // Act
            service.Write(streamBuffer, basicHeader, messageHeader, payloadBuffer.MoveTo(0), (uint)expectedChunkSize);

            // Assert
            using var stream = new ReadOnlyStream(new MemoryStream(streamBuffer.UnderlyingBuffer));
            using var targetBuffer = new NetBuffer();

            var remainingPayloadSize = expectedPayload.Length;

            await AssertFirstChunk(stream);

            while (remainingPayloadSize > 0)
                await AssertRemainingChunk(stream);

            var result = targetBuffer.MoveTo(0).ReadBytes(targetBuffer.Size);
            result.Should().BeEquivalentTo(expectedPayload);

            async Task AssertFirstChunk(ReadOnlyStream stream)
            {
                using var readerBuffer = new NetBuffer(expectedPayload.Length);

                var chunkBasicHeader = await RtmpChunkBasicHeader.ReadAsync(readerBuffer, stream, default);
                chunkBasicHeader.ChunkType.Should().Be(0);
                chunkBasicHeader.ChunkStreamId.Should().Be(expectedChunkStreamId);

                var chunkMessageHeader = await RtmpChunkMessageHeaderType0.ReadAsync(readerBuffer, stream, default);
                chunkMessageHeader.MessageLength.Should().Be(expectedPayload.Length);
                chunkMessageHeader.MessageTypeId.Should().Be(expectedMessageTypeId);
                chunkMessageHeader.MessageStreamId.Should().Be(expectedMessageStreamId);

                if (expectedTimestamp >= 0xffffff)
                {
                    chunkMessageHeader.Timestamp.Should().Be(0xffffff);
                    chunkMessageHeader.HasExtendedTimestamp().Should().BeTrue();
                    var extendedtimestampHeader = await RtmpChunkExtendedTimestampHeader.ReadAsync(readerBuffer, stream, default);
                    extendedtimestampHeader.ExtendedTimestamp.Should().Be(expectedTimestamp);
                }
                else
                {
                    chunkMessageHeader.Timestamp.Should().Be(expectedTimestamp);
                    chunkMessageHeader.HasExtendedTimestamp().Should().BeFalse();
                }

                using var tempBuffer = new NetBuffer();
                await tempBuffer.FromStreamData(stream, Math.Min(expectedChunkSize, remainingPayloadSize));
                targetBuffer.Write(tempBuffer.UnderlyingBuffer, 0, tempBuffer.Size);

                remainingPayloadSize -= expectedChunkSize;
            }

            async Task AssertRemainingChunk(ReadOnlyStream stream)
            {
                using var readerBuffer = new NetBuffer(expectedPayload.Length);

                var chunkBasicHeader = await RtmpChunkBasicHeader.ReadAsync(readerBuffer, stream, default);
                chunkBasicHeader.ChunkType.Should().Be(3);
                chunkBasicHeader.ChunkStreamId.Should().Be(expectedChunkStreamId);

                if (expectedTimestamp >= 0xffffff)
                {
                    var extendedTimestampHeader = await RtmpChunkExtendedTimestampHeader.ReadAsync(readerBuffer, stream, default);
                    extendedTimestampHeader.ExtendedTimestamp.Should().Be(expectedTimestamp);
                }

                using var tempBuffer = new NetBuffer();
                await tempBuffer.FromStreamData(stream, Math.Min(expectedChunkSize, remainingPayloadSize));
                targetBuffer.Write(tempBuffer.UnderlyingBuffer, 0, tempBuffer.Size);

                remainingPayloadSize -= expectedChunkSize;
            }
        }
    }
}
