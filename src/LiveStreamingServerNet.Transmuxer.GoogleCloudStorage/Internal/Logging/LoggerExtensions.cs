﻿using Microsoft.Extensions.Logging;

namespace LiveStreamingServerNet.Transmuxer.GoogleCloudStorage.Internal.Logging
{
    internal static partial class LoggerExtensions
    {
        [LoggerMessage(LogLevel.Error, "An error occurred while uploading TS file | Transmuxer: {Transmuxer} | Identifier: {Identifier} | InputPath: {InputPath} | OutputPath: {OutputPath} | StreamPath: {StreamPath} | TsFilePath: {TsFilePath}")]
        public static partial void UploadingTsFileError(this ILogger logger, string transmuxer, Guid identifier, string inputPath, string outputPath, string streamPath, string tsFilePath, Exception ex);

        [LoggerMessage(LogLevel.Error, "An error occurred while deleting TS file | Transmuxer: {Transmuxer} | Identifier: {Identifier} | InputPath: {InputPath} | OutputPath: {OutputPath} | StreamPath: {StreamPath}")]
        public static partial void DeletingTsFileError(this ILogger logger, string transmuxer, Guid identifier, string inputPath, string outputPath, string streamPath, Exception ex);

        [LoggerMessage(LogLevel.Error, "An error occurred while uploading manifest file | Transmuxer: {Transmuxer} | Identifier: {Identifier} | InputPath: {InputPath} | OutputPath: {OutputPath} | StreamPath: {StreamPath} | ManifestName: {ManifestName}")]
        public static partial void UploadingManifestFileError(this ILogger logger, string transmuxer, Guid identifier, string inputPath, string outputPath, string streamPath, string manifestName, Exception ex);
    }
}
