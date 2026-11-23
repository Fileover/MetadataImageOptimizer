using System.Collections.Generic;

namespace MetadataImageOptimizer.Settings
{
    public class ImageTypeSettings : ObservableObject
    {
        private string format;
        private int maxHeight;
        private int maxWidth;
        private bool optimize;
        private bool lossless;

        public string Format { get => format; set => SetValue(ref format, value); }
        public int MaxHeight { get => maxHeight; set => SetValue(ref maxHeight, value); }
        public int MaxWidth { get => maxWidth; set => SetValue(ref maxWidth, value); }
        public bool Optimize { get => optimize; set => SetValue(ref optimize, value); }

        /// <summary>
        /// Recompress without changing any pixel. Ignored when the target format is JPEG.
        /// </summary>
        public bool Lossless { get => lossless; set => SetValue(ref lossless, value); }
    }
}
