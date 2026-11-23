using System;
using System.Collections.Generic;
using Playnite.SDK.Data;

namespace MetadataImageOptimizer.Settings
{
    public class QualitySettings : ObservableObject
    {
        private int jpgQuality;
        private int pngPreset;
        private int webpQuality;
        private int webpMethod;
        private double jxlDistance;
        private int jxlEffort;
        private bool jxlExpertOptions;
        private int jxlBrotliEffort;
        private bool stripMetadata;
        private bool useZopfli;

        [DontSerialize]
        public Dictionary<int, string> AvailablePngPresets { get; }
            = new Dictionary<int, string>
            {
                { 0, "0 (Fastest)" }
                , { 1, "1" }
                , { 2, "2" }
                , { 3, "3" }
                , { 4, "4 (Default)" }
                , { 5, "5" }
                , { 6, "6 (Slowest, smallest)" }
            };

        [DontSerialize]
        public Dictionary<int, string> AvailableWebpMethods { get; }
            = new Dictionary<int, string>
            {
                { 0, "0 (Fastest)" }
                , { 1, "1" }
                , { 2, "2" }
                , { 3, "3" }
                , { 4, "4" }
                , { 5, "5" }
                , { 6, "6 (Slowest, smallest)" }
            };

        public int JpgQuality
        {
            get => jpgQuality;
            set => SetValue(ref jpgQuality, Math.Min(100, Math.Max(1, value)));
        }

        /// <summary>
        /// OxiPNG compression preset (0-6) used for PNG output.
        /// </summary>
        public int PngPreset
        {
            get => pngPreset;
            set => SetValue(ref pngPreset, Math.Min(6, Math.Max(0, value)));
        }

        public int WebpQuality
        {
            get => webpQuality;
            set => SetValue(ref webpQuality, Math.Min(100, Math.Max(0, value)));
        }

        /// <summary>
        /// WebP compression method (0-6). Higher values take longer and produce smaller files.
        /// </summary>
        public int WebpMethod
        {
            get => webpMethod;
            set => SetValue(ref webpMethod, Math.Min(6, Math.Max(0, value)));
        }

        /// <summary>
        /// JPEG XL Butteraugli distance (0-25). 0 is lossless, higher values trade quality for size.
        /// </summary>
        public double JxlDistance
        {
            get => jxlDistance;
            set => SetValue(ref jxlDistance, Math.Min(25d, Math.Max(0d, value)));
        }

        /// <summary>
        /// JPEG XL effort (1-11). 11 requires the encoder's expert options.
        /// </summary>
        public int JxlEffort
        {
            get => jxlEffort;
            set => SetValue(ref jxlEffort, Math.Min(11, Math.Max(1, value)));
        }

        /// <summary>
        /// Passes cjxl expert options through to the encoder.
        /// </summary>
        public bool JxlExpertOptions
        {
            get => jxlExpertOptions;
            set => SetValue(ref jxlExpertOptions, value);
        }

        /// <summary>
        /// Brotli effort used for the JPEG XL container metadata (0-11).
        /// </summary>
        public int JxlBrotliEffort
        {
            get => jxlBrotliEffort;
            set => SetValue(ref jxlBrotliEffort, Math.Min(11, Math.Max(0, value)));
        }

        /// <summary>
        /// Removes EXIF, XMP, IPTC and comments. ICC profiles are kept.
        /// </summary>
        public bool StripMetadata { get => stripMetadata; set => SetValue(ref stripMetadata, value); }

        /// <summary>
        /// Uses Zopfli for PNG output. Noticeably smaller files, but minutes per image.
        /// </summary>
        public bool UseZopfli { get => useZopfli; set => SetValue(ref useZopfli, value); }
    }
}
