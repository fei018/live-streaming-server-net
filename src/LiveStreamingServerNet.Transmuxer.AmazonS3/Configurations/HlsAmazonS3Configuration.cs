﻿using LiveStreamingServerNet.Transmuxer.AmazonS3.Contracts;

namespace LiveStreamingServerNet.Transmuxer.AmazonS3.Configurations
{
    public class HlsAmazonS3Configuration
    {
        public IHlsObjectPathResolver ObjectPathResolver { get; set; } = new DefaultHlsObjectPathResolver();
        public IHlsObjectUriResolver ObjectUriResolver { get; set; } = new DefaultHlsObjectUriResolver();
    }
}
